// Raw shape of the owner-only group endpoints (GET/DELETE /guest/groups/{groupReservationId}/sessions...).
// The wrapper properties are PascalCase, the dish items are camelCase.

export interface GroupOrderDishApi {
  id: string;
  name: string;
  description: string;
  price: number;
  imageUrl: string | null;
  state: string;
}

export interface GroupOrderItemApi {
  dish: GroupOrderDishApi;
  quantity: number;
}

export interface GroupParticipantApi {
  Id: string | null;
  Type: 'Owner' | 'Guest';
  Name: string;
  Items: GroupOrderItemApi[] | null;
  Total: number;
  IsSubmitted: boolean;
}

export interface GroupSessionsApiResponse {
  Participants: GroupParticipantApi[] | null;
  Total: number;
}
