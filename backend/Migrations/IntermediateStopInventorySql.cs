namespace Travela.API.Migrations;

internal static class IntermediateStopInventorySql
{
    internal const string Preflight = """
        IF EXISTS (SELECT 1 FROM Trips t JOIN Routes r ON r.Id=t.RouteId
                   WHERE t.ArrivalTime<=t.DepartureTime OR t.Price<0.01 OR
                   NULLIF(LTRIM(RTRIM(r.DepartureCity)),N'') IS NULL OR NULLIF(LTRIM(RTRIM(r.ArrivalCity)),N'') IS NULL OR r.DepartureCity=r.ArrivalCity)
            THROW 51010, N'Ara durak geçişi durdu: eski seferin şehir/saat/fiyat verisi bozuk. Otomatik düzeltme yapılmadı.', 1;
        IF EXISTS (SELECT 1 FROM Reservations r JOIN Trips t ON t.Id=r.TripId JOIN Buses b ON b.Id=t.BusId
                   WHERE r.SeatNumber<1 OR r.SeatNumber>COALESCE(t.SeatCapacitySnapshot,b.SeatCapacity))
            THROW 51010, N'Ara durak geçişi durdu: kapasite dışı eski rezervasyon var.', 1;
        IF EXISTS (SELECT 1 FROM PurchaseGroups g JOIN PaymentOrders p ON p.PurchaseGroupId=g.Id JOIN Reservations r ON r.Id=p.ReservationId WHERE g.TripId<>r.TripId)
            THROW 51010, N'Ara durak geçişi durdu: ortak sipariş farklı sefer içeriyor.', 1;
        IF EXISTS (SELECT 1 FROM Reservations WHERE TicketJourneyJson IS NOT NULL AND ISJSON(TicketJourneyJson)<>1)
            THROW 51010, N'Ara durak geçişi durdu: eski bilet anlık görüntüsü okunamıyor.', 1;
        IF EXISTS (SELECT 1 FROM Reservations WHERE TicketJourneyJson IS NOT NULL AND
            (TRY_CONVERT(datetime2,JSON_VALUE(TicketJourneyJson,'$.departureTime'),127) IS NULL OR
             TRY_CONVERT(datetime2,JSON_VALUE(TicketJourneyJson,'$.arrivalTime'),127) IS NULL OR
             TRY_CONVERT(datetime2,JSON_VALUE(TicketJourneyJson,'$.departureTime'),127)>=TRY_CONVERT(datetime2,JSON_VALUE(TicketJourneyJson,'$.arrivalTime'),127)))
            THROW 51010, N'Ara durak geçişi durdu: eski bilet anlık görüntüsünün saatleri geçersiz.', 1;
        IF EXISTS (SELECT 1 FROM Trips t JOIN Buses b ON b.Id=t.BusId WHERE t.AvailableSeats<0 OR
            t.AvailableSeats<>COALESCE(t.SellableSeatCount,b.SeatCapacity)-(SELECT COUNT(*) FROM Reservations r WHERE r.TripId=t.Id AND r.Status=N'Active'))
            THROW 51010, N'Ara durak geçişi durdu: eski kontenjan verisi tutarsız. Otomatik düzeltme yapılmadı.', 1;
        """;

    // Source is the pre-existing trip's two endpoints, NEVER the new RouteStops template.
    // No old passenger, ticket JSON, payment, refund, PNR, price or inventory column is rewritten.
    internal const string Backfill = """
        INSERT TripStops (TripId,StopOrder,City,TerminalId,TerminalName,TerminalAddress,BoardingInstructions,ArrivalTime,DepartureTime,DistanceFromStartKm)
        SELECT t.Id,1,r.DepartureCity,t.DepartureTerminalId,t.DepartureTerminalName,t.DepartureTerminalAddress,t.BoardingInstructions,t.DepartureTime,t.DepartureTime,0
        FROM Trips t JOIN Routes r ON r.Id=t.RouteId;
        INSERT TripStops (TripId,StopOrder,City,TerminalId,TerminalName,TerminalAddress,ArrivalTime,DepartureTime,DistanceFromStartKm)
        SELECT t.Id,2,r.ArrivalCity,t.ArrivalTerminalId,t.ArrivalTerminalName,t.ArrivalTerminalAddress,t.ArrivalTime,t.ArrivalTime,r.DistanceKm
        FROM Trips t JOIN Routes r ON r.Id=t.RouteId;
        INSERT TripLegs (TripId,LegNumber,FromStopOrder,ToStopOrder) SELECT Id,1,1,2 FROM Trips;
        INSERT TripSegmentFares (TripId,FromStopOrder,ToStopOrder,Amount,Source,IsApproved,ApprovedAt)
        SELECT Id,1,2,Price,N'FullRoute',1,SYSUTCDATETIME() FROM Trips;
        UPDATE r SET BoardingStopOrder=1,AlightingStopOrder=2,
            BoardingDepartureSnapshot=COALESCE(TRY_CONVERT(datetime2,JSON_VALUE(r.TicketJourneyJson,'$.departureTime'),127),t.DepartureTime),
            AlightingArrivalSnapshot=COALESCE(TRY_CONVERT(datetime2,JSON_VALUE(r.TicketJourneyJson,'$.arrivalTime'),127),t.ArrivalTime)
        FROM Reservations r JOIN Trips t ON t.Id=r.TripId;
        UPDATE PurchaseGroups SET BoardingStopOrder=1,AlightingStopOrder=2;
        INSERT ReservationLegs (ReservationId,TripId,SeatNumber,LegNumber,IsActive)
        SELECT Id,TripId,SeatNumber,1,CASE WHEN Status=N'Active' THEN 1 ELSE 0 END FROM Reservations;
        IF (SELECT COUNT_BIG(*) FROM TripStops) <> 2*(SELECT COUNT_BIG(*) FROM Trips) OR
           (SELECT COUNT_BIG(*) FROM ReservationLegs) <> (SELECT COUNT_BIG(*) FROM Reservations) OR
           EXISTS (SELECT 1 FROM Reservations r LEFT JOIN ReservationLegs l ON l.ReservationId=r.Id
                   WHERE l.ReservationId IS NULL OR l.IsActive<>CASE WHEN r.Status=N'Active' THEN 1 ELSE 0 END)
            THROW 51010, N'Ara durak geçişinin satır sayıları veya aktiflik doğrulaması başarısız; işlem geri alındı.', 1;
        """;

    internal const string Reservations = """
        CREATE OR ALTER TRIGGER TR_Reservations_Legs ON Reservations AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF TRIGGER_NESTLEVEL(OBJECT_ID(N'TR_Reservations_Legs'), 'AFTER', 'DML')>1 RETURN;
            IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE
                i.TripId<>d.TripId OR i.SeatNumber<>d.SeatNumber OR
                (d.BoardingStopOrder IS NOT NULL AND (i.BoardingStopOrder IS NULL OR i.AlightingStopOrder IS NULL OR
                 i.BoardingStopOrder<>d.BoardingStopOrder OR i.AlightingStopOrder<>d.AlightingStopOrder OR
                 i.BoardingDepartureSnapshot IS NULL OR i.AlightingArrivalSnapshot IS NULL OR
                 i.BoardingDepartureSnapshot<>d.BoardingDepartureSnapshot OR i.AlightingArrivalSnapshot<>d.AlightingArrivalSnapshot)))
                THROW 51011, N'Rezervasyonun sefer, koltuk ve segment anlık görüntüsü değiştirilemez.', 1;
            UPDATE r SET BoardingStopOrder=1,AlightingStopOrder=s.LastStop,
                BoardingDepartureSnapshot=a.DepartureTime,AlightingArrivalSnapshot=b.ArrivalTime
            FROM Reservations r JOIN inserted i ON i.Id=r.Id
            CROSS APPLY (SELECT MAX(StopOrder) LastStop FROM TripStops WHERE TripId=r.TripId) s
            JOIN TripStops a ON a.TripId=r.TripId AND a.StopOrder=1
            JOIN TripStops b ON b.TripId=r.TripId AND b.StopOrder=s.LastStop
            WHERE r.BoardingStopOrder IS NULL;
            IF EXISTS (SELECT 1 FROM Reservations r JOIN inserted i ON i.Id=r.Id
                WHERE r.BoardingStopOrder IS NULL OR r.AlightingStopOrder IS NULL OR
                r.AlightingStopOrder<=r.BoardingStopOrder OR
                (SELECT COUNT(*) FROM TripStops s WHERE s.TripId=r.TripId)<>(SELECT MAX(StopOrder) FROM TripStops s WHERE s.TripId=r.TripId) OR
                (SELECT COUNT(*) FROM TripLegs l WHERE l.TripId=r.TripId)<>(SELECT MAX(StopOrder)-1 FROM TripStops s WHERE s.TripId=r.TripId) OR
                (SELECT COUNT(*) FROM TripLegs l WHERE l.TripId=r.TripId AND l.LegNumber>=r.BoardingStopOrder AND l.LegNumber<r.AlightingStopOrder)<>r.AlightingStopOrder-r.BoardingStopOrder OR
                EXISTS (SELECT 1 FROM TripStops a JOIN TripStops b ON a.TripId=b.TripId AND b.StopOrder=a.StopOrder+1 WHERE a.TripId=r.TripId AND a.DepartureTime>=b.ArrivalTime))
                THROW 51011, N'Rezervasyon segmentinin bacakları veya saatleri eksik/tutarsız.', 1;
            IF EXISTS (SELECT 1 FROM Reservations r JOIN inserted i ON i.Id=r.Id
                JOIN TripStops a ON a.TripId=r.TripId AND a.StopOrder=r.BoardingStopOrder
                JOIN TripStops b ON b.TripId=r.TripId AND b.StopOrder=r.AlightingStopOrder
                WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.Id=r.Id) AND
                (r.BoardingDepartureSnapshot<>a.DepartureTime OR r.AlightingArrivalSnapshot<>b.ArrivalTime))
                THROW 51011, N'Yeni rezervasyonun biniş ve iniş saatleri durak anlık görüntüsüyle eşleşmelidir.', 1;
            INSERT ReservationLegs (ReservationId,TripId,SeatNumber,LegNumber,IsActive)
            SELECT r.Id,r.TripId,r.SeatNumber,l.LegNumber,CASE WHEN r.Status=N'Active' THEN 1 ELSE 0 END
            FROM Reservations r JOIN inserted i ON i.Id=r.Id JOIN TripLegs l ON l.TripId=r.TripId
                AND l.LegNumber>=r.BoardingStopOrder AND l.LegNumber<r.AlightingStopOrder
            WHERE NOT EXISTS (SELECT 1 FROM ReservationLegs old WHERE old.ReservationId=r.Id AND old.LegNumber=l.LegNumber);
            UPDATE l SET IsActive=CASE WHEN r.Status=N'Active' THEN 1 ELSE 0 END
            FROM ReservationLegs l JOIN Reservations r ON r.Id=l.ReservationId JOIN inserted i ON i.Id=r.Id
            WHERE l.IsActive<>CASE WHEN r.Status=N'Active' THEN 1 ELSE 0 END;
        END
        """;

    internal const string Legs = """
        CREATE OR ALTER TRIGGER TR_ReservationLegs_Consistency ON ReservationLegs AFTER INSERT, UPDATE, DELETE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS (SELECT 1 FROM Reservations r JOIN
                (SELECT ReservationId FROM inserted UNION SELECT ReservationId FROM deleted) changed ON changed.ReservationId=r.Id
                WHERE (SELECT COUNT(*) FROM ReservationLegs l WHERE l.ReservationId=r.Id)<>r.AlightingStopOrder-r.BoardingStopOrder OR
                EXISTS (SELECT 1 FROM ReservationLegs l WHERE l.ReservationId=r.Id AND
                    (l.TripId<>r.TripId OR l.SeatNumber<>r.SeatNumber OR l.LegNumber<r.BoardingStopOrder OR l.LegNumber>=r.AlightingStopOrder OR
                     l.IsActive<>CASE WHEN r.Status=N'Active' THEN 1 ELSE 0 END)))
                THROW 51011, N'Rezervasyon bacakları eksiltilemez, taşınamaz veya bağımsız etkinleştirilemez.', 1;
        END
        """;

    internal const string Stops = """
        CREATE OR ALTER TRIGGER TR_TripStops_Topology ON TripStops AFTER INSERT, UPDATE, DELETE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS (SELECT 1 FROM (
                SELECT TripId,StopOrder,City,TerminalId,TerminalName,TerminalAddress,BoardingInstructions FROM inserted
                EXCEPT SELECT TripId,StopOrder,City,TerminalId,TerminalName,TerminalAddress,BoardingInstructions FROM deleted
            ) x WHERE EXISTS (SELECT 1 FROM Reservations r WHERE r.TripId=x.TripId)) OR
            EXISTS (SELECT 1 FROM (
                SELECT TripId,StopOrder,City,TerminalId,TerminalName,TerminalAddress,BoardingInstructions FROM deleted
                EXCEPT SELECT TripId,StopOrder,City,TerminalId,TerminalName,TerminalAddress,BoardingInstructions FROM inserted
            ) x WHERE EXISTS (SELECT 1 FROM Reservations r WHERE r.TripId=x.TripId))
                THROW 51012, N'Rezervasyon geçmişi olan seferin durak topolojisi ve terminal anlık görüntüsü kilitli.', 1;
        END
        """;

    internal const string TripLegs = """
        CREATE OR ALTER TRIGGER TR_TripLegs_Topology ON TripLegs AFTER INSERT, UPDATE, DELETE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS (SELECT 1 FROM (SELECT TripId FROM inserted UNION SELECT TripId FROM deleted) x
                WHERE EXISTS (SELECT 1 FROM Reservations r WHERE r.TripId=x.TripId))
                THROW 51012, N'Rezervasyon geçmişi olan seferin bacakları değiştirilemez.', 1;
        END
        """;

    internal const string Groups = """
        CREATE OR ALTER TRIGGER TR_PurchaseGroups_Segment ON PurchaseGroups AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF TRIGGER_NESTLEVEL(OBJECT_ID(N'TR_PurchaseGroups_Segment'), 'AFTER', 'DML')>1 RETURN;
            UPDATE g SET BoardingStopOrder=1,AlightingStopOrder=s.LastStop
            FROM PurchaseGroups g JOIN inserted i ON i.Id=g.Id
            CROSS APPLY (SELECT MAX(StopOrder) LastStop FROM TripStops WHERE TripId=g.TripId) s
            WHERE g.BoardingStopOrder IS NULL AND g.AlightingStopOrder IS NULL;
            IF EXISTS (SELECT 1 FROM PurchaseGroups g JOIN inserted i ON i.Id=g.Id
                WHERE g.BoardingStopOrder IS NULL OR g.AlightingStopOrder IS NULL OR g.BoardingStopOrder>=g.AlightingStopOrder OR
                EXISTS (SELECT 1 FROM PaymentOrders p JOIN Reservations r ON r.Id=p.ReservationId WHERE p.PurchaseGroupId=g.Id AND
                    (r.TripId<>g.TripId OR r.BoardingStopOrder<>g.BoardingStopOrder OR r.AlightingStopOrder<>g.AlightingStopOrder)))
                THROW 51011, N'Ortak siparişin sefer ve segmenti tutarlı olmalıdır.', 1;
        END
        """;

    internal const string Orders = """
        CREATE OR ALTER TRIGGER TR_PaymentOrders_Segment ON PaymentOrders AFTER INSERT, UPDATE AS
        BEGIN
            SET NOCOUNT ON;
            IF EXISTS (SELECT 1 FROM inserted i JOIN PurchaseGroups g ON g.Id=i.PurchaseGroupId JOIN Reservations r ON r.Id=i.ReservationId
                WHERE r.TripId<>g.TripId OR r.BoardingStopOrder<>g.BoardingStopOrder OR r.AlightingStopOrder<>g.AlightingStopOrder)
                THROW 51011, N'Ortak siparişin bütün yolcuları aynı sefer ve segmentte olmalıdır.', 1;
        END
        """;
}
