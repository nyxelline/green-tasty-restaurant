# Test Cases – Green & Tasty

Format follows the course template (ID/Title/Status, Pre-conditions, Steps, Expected Result),
covering the user stories in `user-stories.md`.

---

**ID: TC-01** – Verify a guest with a duplicate name is disambiguated, including against the owner's own name
**Status:** Passed
**Covers:** US03

**Pre-conditions:**
Group reservation is `Open`; owner's display name is known (e.g. "Ria Fane").

**Steps:**
1. Open the invite link as Guest A, enter name "Ria Fane" (same as the owner's real name), join.
2. Open the invite link again as Guest B, also enter "Ria Fane", join.

**Expected result:**
1. Guest A is registered as "Ria Fane (2)" (or similar), not literally "Ria Fane", since that name is already taken by the owner's display name.
2. Guest B is registered as "Ria Fane (3)".
3. The group's participant list and bill show three distinct entries – no two participants are visually indistinguishable.

---

**ID: TC-02** – Verify a new guest can join after the owner raises the guest count on an already-submitted group
**Status:** Passed
**Covers:** US02

**Pre-conditions:**
Group reservation status is `Submitted` (pre-order already finalized); reservation is `Reserved`.

**Steps:**
1. Owner edits the reservation and increases guest count from e.g. 3 to 4.
2. Owner fetches the invite link again and shares it with a new guest.
3. New guest opens the link and attempts to join.

**Expected result:**
1. Group status reopens to `Open` as a result of the guest-count increase.
2. The invite link is retrievable (not blocked by "group already submitted").
3. The new guest successfully joins and can build a pre-order.

---

**ID: TC-03** – Verify the owner can pre-order immediately after creating a reservation (no navigation detour required)
**Status:** Passed
**Covers:** US01, US04

**Pre-conditions:**
Customer has just completed booking a table (reservation created this session, not loaded from "My Reservations").

**Steps:**
1. From the booking-confirmation screen, click "Make Pre-Order".

**Expected result:**
Customer is taken straight to the Menu page pre-scoped to the new reservation (reservation id/label carried through), identical to what happens via "My Reservations → Pre-order".

---

**ID: TC-04** – Verify a cancelled dish is shown as rejected rather than silently disappearing
**Status:** Passed
**Covers:** US04

**Pre-conditions:**
Guest has a submitted pre-order; owner has removed one dish from that guest's cart.

**Steps:**
1. As the guest, open "My Pre-Order" / submitted-order view after the owner's edit.

**Expected result:**
1. The removed dish is still listed, shown with a "Rejected" badge, not dropped from the view.
2. Every dish in the response carries an actual state (Pending / Confirmed / Rejected) – none show as "(Unknown)".

---

**ID: TC-05** – Verify the booking time-slot grid only shows a window around the selected time
**Status:** Passed
**Covers:** US01

**Pre-conditions:**
Customer is on "Book a Table" with a location and time selected (e.g. 18:00).

**Steps:**
1. Observe the per-table time grid after selecting a time.

**Expected result:**
Grid shows only a window around the selected time (±2h), not every slot for the entire day.

---

**ID: TC-06** – Verify a waiter cannot update the status of a reservation assigned to a different waiter
**Status:** Passed
**Covers:** US05

**Pre-conditions:**
Two waiters, A and B, are both authenticated; reservation R is assigned to waiter A.

**Steps:**
1. As waiter B, call update-status on reservation R.

**Expected result:**
Request is rejected with 403 ("You can only update the status of reservations assigned to you.").
