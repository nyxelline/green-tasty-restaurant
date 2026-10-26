import { GroupSessionsApiResponse } from '@app/models/group-order/group-sessions-api.model';
import { GroupSessions } from '@app/models/group-order/group-sessions.model';

const AVAILABLE_STATE = 'Available';

export function mapGroupSessions(response: GroupSessionsApiResponse): GroupSessions {
  const participants = (response.Participants ?? []).map((participant) => ({
    id: participant.Id ?? '',
    type: participant.Type,
    name: participant.Name,
    total: participant.Total,
    isSubmitted: participant.IsSubmitted,
    items: (participant.Items ?? []).map((item) => ({
      quantity: item.quantity,
      dish: {
        id: item.dish.id,
        name: item.dish.name,
        price: item.dish.price,
        imageUrl: item.dish.imageUrl ?? null,
        isAvailable: item.dish.state === AVAILABLE_STATE,
      },
    })),
  }));

  return { participants, total: response.Total };
}
