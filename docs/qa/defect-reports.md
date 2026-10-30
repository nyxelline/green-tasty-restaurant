# Defect Reports – Green & Tasty

Format follows the course template (ID/Summary/Env-Priority-Severity, Pre-conditions, Steps/Actual/Expected).
All defects below were found during manual testing of the deployed build and have since been fixed;
`Status` reflects the state at the time of writing.

---

**ID: DR-01** – Submitted pre-order dish shows "(Unknown)" status instead of its real state
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Customer has a submitted pre-order with at least one dish.

**Steps:**
1. Open "My Pre-Order" after submitting.
2. Look at the status badge on any dish line.

**Actual result:** Badge shows "(Unknown)" for every dish.
**Expected result:** Badge shows the dish's real state (Pending / Confirmed / Rejected).

**Root cause:** The backend response model (`OrderItemResponse`) had no `state` field at all – the frontend always received `undefined`.

---

**ID: DR-02** – "Added to X's table" message shows the guest's own name instead of the owner's
**Env:** P_PRD | **Priority:** Medium | **Severity:** Minor | **Status:** Fixed

**Pre-conditions:** Guest has joined a group session and submitted dishes.

**Steps:**
1. As a guest named e.g. "Alex", submit the cart.
2. Read the confirmation message.

**Actual result:** "Your selections have been added to Alex's table" (guest's own name).
**Expected result:** "...added to [owner's name]'s table" – it's the owner's table, not the guest's.

---

**ID: DR-03** – Owner cannot pre-order immediately after creating a reservation
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Customer has just booked a table in the current session.

**Steps:**
1. From the booking-confirmation modal, click "Make Pre-Order".
2. Observe the Menu page.

**Actual result:** Menu page opens with no reservation context – pre-order cannot be started. Closing and reopening via "My Reservations" works fine.
**Expected result:** Pre-order starts immediately, same as via "My Reservations".

**Root cause:** The confirmation modal navigated to `/menu` without the `reservationId`/`reservationLabel` query params the Menu page needs.

---

**ID: DR-04** – Two guests can join a group reservation with the exact same name, including the owner's name
**Env:** P_PRD | **Priority:** Medium | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Group reservation is `Open`.

**Steps:**
1. Join the group twice with the same guest name (or with the owner's real name).

**Actual result:** Two indistinguishable participant rows; a guest using the owner's own name is indistinguishable from the owner in the bill/participant list.
**Expected result:** A repeated name is automatically suffixed (`Name (2)`, `Name (3)`, ...), including when it collides with the owner's display name.

---

**ID: DR-05** – New guest cannot join after the owner raises guest count on an already-submitted group
**Env:** P_PRD | **Priority:** High | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Group reservation status is `Submitted`.

**Steps:**
1. Owner increases the reservation's guest count.
2. Owner tries to fetch the invite link / a new guest tries to join.

**Actual result:** Invite link fetch and join both fail – group reservation stays locked as `Submitted`.
**Expected result:** Increasing guest count reopens the group (`Open`) so new guests can join.

---

**ID: DR-06** – Booking time-slot grid shows the whole day regardless of the selected time
**Env:** P_PRD | **Priority:** Low | **Severity:** Minor | **Status:** Fixed

**Pre-conditions:** Customer is booking a table and has selected a specific time.

**Steps:**
1. Select a time, e.g. 18:00, while browsing tables.
2. Look at the per-table time grid.

**Actual result:** Grid shows every slot for the entire day, making the selected time hard to find and the filter feel ineffective.
**Expected result:** Grid centers on the selected time with a reasonable window around it (±2h), not the full day.

---

**ID: DR-07** – Cancelled pre-order dishes silently disappear from the guest's own view
**Env:** P_PRD | **Priority:** Medium | **Severity:** Major | **Status:** Fixed

**Pre-conditions:** Owner has removed/rejected a dish from a guest's submitted cart.

**Steps:**
1. As the guest, reload the pre-order/cart view.

**Actual result:** The rejected dish is filtered out entirely – the guest has no way to see what was removed.
**Expected result:** The dish stays visible, flagged as "Rejected", so the guest knows what changed and why their total is lower.

---

**ID: DR-08** – Waiter could update the status of reservations not assigned to them
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Fixed

**Pre-conditions:** Two waiters exist; a reservation is assigned to waiter A only.

**Steps:**
1. As waiter B, call the update-status endpoint for that reservation.

**Actual result:** Request succeeds – any waiter could change the status of any table, not just their own.
**Expected result:** Only the assigned waiter can update the reservation's status; others get 403.

---

**ID: DR-09** – Waiter's reservation list fails entirely if a single reservation record can't be converted
**Env:** P_PRD | **Priority:** High | **Severity:** Critical | **Status:** Open

**Pre-conditions:** Waiter is authenticated; at least one reservation assigned to them has data that fails during response conversion (e.g. a dangling `TableId` whose table/location no longer exists, or an order lookup failure).

**Steps:**
1. Sign in as a waiter whose assigned reservations include at least one such "bad" record.
2. Open the waiter dashboard ("Reservations").

**Actual result:** `GET /reservations` returns `500 {"error":"Internal Server Error: Error while fetching reservation details."}`. The UI shows the contradictory "You have 0 reservations for any date, any time" together with "Reservations could not be loaded." The waiter cannot see **any** of their reservations, including the healthy ones.
**Expected result:** One bad reservation record should not take down the whole list. The waiter should see every reservation that converts successfully, with the broken one flagged or omitted — matching how the customer-facing path already behaves (see Root cause).

**Root cause:** `GetReservationsForUserHandler.cs` handles the two roles inconsistently:
- `GetReservationsForCustomerAsync` wraps each reservation's conversion in try/catch and `continue`s past any that throw — a customer always gets a partial, degraded list.
- `GetReservationsForWaiterAsync` does the opposite: the same per-item try/catch immediately `return`s a 500 for the **entire request** the moment any single reservation throws in `ConvertToResponse`.

**Suggested fix:** Make `GetReservationsForWaiterAsync` skip (and log) a failing reservation the same way the customer path does, instead of aborting the whole response.


---

**ID: DR-10** – "Most Popular Dishes" / browsing menu mixes all locations together with no dedup, making distinct dishes look duplicated
**Env:** P_PRD | **Priority:** Medium | **Severity:** Minor | **Status:** Open

**Pre-conditions:** More than one location has a dish with the same name (e.g. "Margherita Pizza" exists at both 14 Baratashvili Street and 48 Rustaveli Avenue).

**Steps:**
1. Open the main page or the Menu page while browsing (no reservation/location context selected).
2. Look at "Most Popular Dishes" / the dish grid.

**Actual result:** The same dish name appears twice (e.g. "Margherita Pizza" and "Grilled Salmon" each shown twice) with no location label to tell them apart, which reads as a duplicate-rendering bug. Conversely, because the list is an unfiltered dump of every location's dishes rather than a true top-N "popular" ranking, dishes a user expects to see can appear to be missing depending on sort order.
**Expected result:** Either scope the list to one location (consistent with the rest of the app, which is location-scoped everywhere else), or show a location label per dish and genuinely rank/dedupe by popularity instead of returning a raw table scan.

**Root cause:** `GetPopularDishesHandler.cs` does `_dbContext.ScanAsync<Dish>(new List<ScanCondition>(), _dishesConfig)` — an unfiltered scan of the entire Dishes table across all locations, mapped straight to the response with no location filter, no popularity ranking, and no de-duplication by name.

## Known Issues (not fixed / out of scope for this submission)

These were observed during manual testing but are either unimplemented features or low-severity UX gaps rather than regressions in existing functionality.

- **"Forgot password?" link is dead.** On the Sign In modal, the "Forgot password?" link has `href="#"` with no handler — clicking it does nothing. Password-reset flow was never implemented.
- **Cancelling a reservation has no confirmation step.** Clicking "Cancel" on "My Reservations" cancels immediately with no "Are you sure?" dialog, even though the action is irreversible (no un-cancel / re-book shortcut).
- **Dish `fats` value always displays as "0 g".** `DishListResponse.Fats` was mapped with `[DynamoDBProperty("fats")]` instead of `[JsonPropertyName("fats")]`, so the API serialized it as `"Fats"` (capitalized) while every other nutrition field used the correct lowercase key — the frontend never sees the real value. **Fix is written** (swapped to `[JsonPropertyName("fats")]`) but **not yet deployed** as of this writing; re-verify after the next `syndicate build`/`syndicate update`.
