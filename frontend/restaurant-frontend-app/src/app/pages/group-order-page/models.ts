export type GuestStatusKind = 'confirmed' | 'choosing' | 'empty' | 'attention' | 'changed';

export type BannerKind = 'info' | 'warn' | 'error';

export type LoadState = 'loading' | 'ready' | 'error';

export interface BannerView {
  kind: BannerKind;
  text: string;
}

export interface DishRowView {
  dishId: string;
  name: string;
  imageUrl: string | null;
  quantity: number;
  lineTotal: number;
  isUnavailable: boolean;
  isRemoved: boolean;
}

export interface GuestView {
  id: string;
  name: string;
  initial: string;
  statusKind: GuestStatusKind;
  statusLabel: string;
  total: number;
  isSubmitted: boolean;
  hasItems: boolean;
  isExpanded: boolean;
  isConfirmingRemoval: boolean;
  rows: DishRowView[];
}

export interface BillRowView {
  id: string;
  name: string;
  initial: string;
  isOwner: boolean;
  isSubmitted: boolean;
  summary: string;
  subtotal: number;
  sharePercent: number;
}
