export type PassengerDetails = { fullName: string; identityNumber: string };
export type CheckoutProfile = PassengerDetails & { email: string; phone: string };
type PassengerEntry = PassengerDetails & { edited: Record<keyof PassengerDetails, boolean> };
type Contact = { email: string; phone: string };
export type PassengerDetailsState = {
  passengers: Record<number, PassengerEntry>;
  contact: Contact;
  contactEdited: Record<keyof Contact, boolean>;
  profile: CheckoutProfile | null;
  autoFillClaimed: boolean;
  autoFillSeat: number | null;
};
type Action =
  | { type: "seats"; seats: number[] }
  | { type: "profile"; profile: CheckoutProfile }
  | { type: "passenger"; seat: number; field: keyof PassengerDetails; value: string }
  | { type: "contact"; field: keyof Contact; value: string }
  | { type: "clear"; seat: number }
  | { type: "use-profile"; seat: number };

const blankPassenger = (): PassengerEntry => ({ fullName: "", identityNumber: "", edited: { fullName: false, identityNumber: false } });
export function initialPassengerDetails(): PassengerDetailsState {
  return { passengers: {}, contact: { email: "", phone: "" }, contactEdited: { email: false, phone: false },
    profile: null, autoFillClaimed: false, autoFillSeat: null };
}

function autoFill(state: PassengerDetailsState): PassengerDetailsState {
  const seat = state.autoFillSeat;
  if (seat === null || !state.profile || !state.passengers[seat]) return state;
  const passenger = state.passengers[seat];
  return { ...state, passengers: { ...state.passengers, [seat]: { ...passenger,
    fullName: passenger.edited.fullName ? passenger.fullName : state.profile.fullName,
    identityNumber: passenger.edited.identityNumber ? passenger.identityNumber : state.profile.identityNumber,
  } } };
}

// This state is kept only in component memory. Passenger data is always keyed by seat number.
export function passengerDetailsReducer(state: PassengerDetailsState, action: Action): PassengerDetailsState {
  switch (action.type) {
    case "seats": {
      const firstSelection = !state.autoFillClaimed && action.seats.length > 0;
      return autoFill({ ...state,
        passengers: Object.fromEntries(action.seats.map(seat => [seat, state.passengers[seat] ?? blankPassenger()])),
        autoFillClaimed: state.autoFillClaimed || firstSelection,
        autoFillSeat: firstSelection ? action.seats[0]
          : state.autoFillSeat !== null && action.seats.includes(state.autoFillSeat) ? state.autoFillSeat : null,
      });
    }
    case "profile":
      return autoFill({ ...state, profile: action.profile, contact: {
        email: state.contactEdited.email ? state.contact.email : action.profile.email,
        phone: state.contactEdited.phone ? state.contact.phone : action.profile.phone,
      } });
    case "contact":
      return { ...state, contact: { ...state.contact, [action.field]: action.value },
        contactEdited: { ...state.contactEdited, [action.field]: true } };
    case "passenger": {
      const passenger = state.passengers[action.seat] ?? blankPassenger();
      return { ...state, passengers: { ...state.passengers, [action.seat]: { ...passenger,
        [action.field]: action.value, edited: { ...passenger.edited, [action.field]: true },
      } } };
    }
    case "clear":
      return { ...state, passengers: { ...state.passengers, [action.seat]: {
        fullName: "", identityNumber: "", edited: { fullName: true, identityNumber: true },
      } } };
    case "use-profile": {
      if (!state.profile) return state;
      const passenger = state.passengers[action.seat] ?? blankPassenger();
      return { ...state, passengers: { ...state.passengers, [action.seat]: {
        fullName: state.profile.fullName || passenger.fullName,
        identityNumber: state.profile.identityNumber || passenger.identityNumber,
        edited: { fullName: true, identityNumber: true },
      } } };
    }
  }
}
