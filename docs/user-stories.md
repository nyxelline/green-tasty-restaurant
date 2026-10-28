# User Stories — Green & Tasty

Format follows the course template (Epic / US name / Role / Description / Pre-conditions / Acceptance Criteria),
applied to this project's actual implemented features.

---

## US01 — Book a Table

| | |
|---|---|
| **Epic** | Reservations |
| **US name** | Customer Table Booking |
| **Role** | Customer |
| **Description** | As a Customer, I want to search for and book an available table by location, date, time and party size, so that I can secure a spot at the restaurant. |

**Pre-conditions:**
- Customer has an account and is signed in.
- At least one location has a table free for the requested date/time/party size.

**Acceptance Criteria (AC):**
1. Customer can filter available tables by location, date, time and number of guests.
2. Only tables that fit the requested party size and are free in that time window are offered.
3. Confirming a table creates a Reservation with status `Reserved`.
4. If the party size is 2 or more, a Group Reservation is created automatically, enabling the invite-link flow (US02).
5. The new reservation appears under "My Reservations" immediately after booking.

---

## US02 — Invite Guests to a Group Pre-Order

| | |
|---|---|
| **Epic** | Order Management |
| **US name** | Owner Generates Group Invite Link |
| **Role** | Customer (reservation owner) |
| **Description** | As the owner of a reservation, I want to generate a shareable invite link for my table, so that other guests can join and build their own pre-order before the visit. |

**Pre-conditions:**
- Reservation exists with status `Reserved` and guest count ≥ 2.
- Caller is the reservation's owner (or its assigned waiter).

**Acceptance Criteria (AC):**
1. Only the reservation's owner or assigned waiter can retrieve the invite link/token — no other authenticated user can.
2. The link only works while the group's status is `Open` and the reservation itself is still `Reserved`.
3. If the owner raises the guest count after the group was already finalized (`Submitted`) or closed, the group reopens for new joins rather than staying locked.
4. The number of guests who can join is capped at `GuestCount − 1` (the remaining seat is the owner's); joining past the cap is rejected with a clear message.

---

## US03 — Guest Joins and Builds a Pre-Order

| | |
|---|---|
| **Epic** | Order Management |
| **US name** | Guest Joins Group Session and Views Menu |
| **Role** | Guest |
| **Description** | As a Guest, I want to join a group reservation through an invite link, enter my name, and browse the menu, so that I can build and submit my own pre-order. |

**Pre-conditions:**
- Guest has a valid, unexpired invite link.
- Reservation status is `Reserved`; group status is `Open`; a guest slot is still available.

**Acceptance Criteria (AC):**
1. Guest must enter a name before the menu becomes browsable.
2. If the entered name collides with another guest already in the group — or with the owner's own display name — it is automatically disambiguated with a suffix (e.g. `Alex (2)`), instead of silently merging the two.
3. The full menu is shown grouped by category, with name, price, weight and current availability per dish.
4. A dish marked unavailable ("Out of Stock") cannot be added to the cart.
5. The guest can submit their own cart independently of other guests; once submitted, edits are locked until the owner changes something in that guest's cart.
6. Pre-order changes are blocked starting 30 minutes before the reservation's start time, for guests exactly as for the owner.

---

## US04 — Owner Reviews the Group Order and Bill

| | |
|---|---|
| **Epic** | Order Management |
| **US name** | Owner Consolidates Group Pre-Order |
| **Role** | Customer (reservation owner) |
| **Description** | As the table owner, I want to see every guest's submitted cart alongside my own and a combined bill, so that I can review and send one consolidated pre-order to the kitchen. |

**Pre-conditions:**
- At least the owner's own cart, or one guest session, exists for the group.
- Caller is the reservation's owner.

**Acceptance Criteria (AC):**
1. Owner sees a per-participant breakdown (herself + every guest) with each one's items and a running total, plus a combined group total.
2. Owner can remove an individual dish from a guest's cart; the state is kept (not deleted) so the guest sees what was removed.
3. If the owner edits a guest's already-submitted cart, that guest's "submitted" flag is cleared, since the contents no longer match what the guest actually confirmed.
4. The owner's own order (placed through the normal pre-order flow) is shown in the same list as a participant labelled "Owner", not as a separate, disconnected view.
5. A dish that was cancelled stays visible with a "Rejected" state rather than disappearing from the guest's view.

---

## US05 — Waiter Manages Assigned Reservations

| | |
|---|---|
| **Epic** | Waiter Dashboard |
| **US name** | Waiter Updates Status for Own Reservations Only |
| **Role** | Waiter |
| **Description** | As a Waiter, I want to see and manage only the reservations assigned to me, so that I can update table status and confirm orders without affecting tables I'm not serving. |

**Pre-conditions:**
- Waiter is signed in and has one or more reservations assigned.

**Acceptance Criteria (AC):**
1. Waiter's dashboard lists only reservations assigned to them.
2. Waiter can change a reservation's status only when it is assigned to them — attempting to update a reservation assigned to a different waiter is rejected (403).
3. Waiter can view and confirm/cancel individual dishes in an order tied to a reservation assigned to them, or belonging to the customer who placed it; not any reservation in the system.
4. Report generation/download and business reports are available to Waiters only, not to Customers.
