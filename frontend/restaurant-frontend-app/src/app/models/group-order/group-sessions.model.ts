export type GroupParticipantType = 'Owner' | 'Guest';

export interface GroupOrderDish {
  id: string;
  name: string;
  price: number;
  imageUrl: string | null;
  isAvailable: boolean;
}

export interface GroupOrderItem {
  dish: GroupOrderDish;
  quantity: number;
}

export interface GroupParticipant {
  id: string;
  type: GroupParticipantType;
  name: string;
  items: GroupOrderItem[];
  total: number;
  isSubmitted: boolean;
}

export interface GroupSessions {
  participants: GroupParticipant[];
  total: number;
}
