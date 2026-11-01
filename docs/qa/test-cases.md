# Test Cases – Green & Tasty

Format follows the course template (ID/Title/Status, Pre-conditions, Steps, Expected Result),
covering the user stories in `user-stories.md`. This is a curated set of the 7 test cases matching
the 7 representative defects in `defect-reports.md`.

---

**ID: TC-01** – Verify a waiter cannot update the status of a reservation assigned to a different waiter
**Status:** Passed
**Covers:** US05

**Pre-conditions:**
Two waiters, A and B, are both authenticated; reservation R is assigned to waiter A.

**Steps:**
1. As waiter B, call update-status on reservation R.

**Expected result:**
Request is rejected with 403 ("You can only update the status of reservations assigned to you.").

**Actual result:**
Matches expected. See DR-01.

---

**ID: TC-02** – Verify one bad reservation record does not take down a waiter's entire reservation list
**Status:** Passed
**Covers:** US05

**Pre-conditions:**
Waiter is authenticated; among their assigned reservations, at least one record fails response conversion (e.g. dangling `TableId`, or an order-lookup error).

**Steps:**
1. Sign in as the affected waiter.
2. Open the waiter dashboard ("Reservations").

**Expected result:**
The waiter sees every reservation that converts successfully; the one broken record is skipped (and logged), not surfaced as a blanket failure.

**Actual result:**
The broken reservation is now skipped (logged) and the waiter sees every other reservation normally. See DR-02.

---

**ID: TC-03** – Verify a new guest can join after the owner raises the guest count on an already-submitted group
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

**Actual result:**
Matches expected. See DR-03.

---

**ID: TC-04** – Verify the group owner can submit the consolidated order without having pre-ordered personally
**Status:** Passed
**Covers:** US02

**Pre-conditions:**
Group reservation has at least one guest with a submitted pre-order; owner has never opened the menu for this reservation.

**Steps:**
1. As the owner, open the group's consolidated view and click "Submit Group Order".

**Expected result:**
Submit succeeds (`200`); the consolidated order contains the guests' dishes; group status becomes `Submitted`.

**Actual result:**
Submit now succeeds and auto-creates an empty draft order for the owner to merge guest dishes into. See DR-04.

---

**ID: TC-05** – Verify editing a reservation near month-end to a date in a shorter month saves the correct date
**Status:** Passed
**Covers:** US01

**Pre-conditions:**
Current date has a day-of-month that doesn't exist in the target month (e.g. editing on the 31st to a 30-day month).

**Steps:**
1. Open "Edit Reservation" and change the date/time to the 3rd of the following month, 11:00 AM.
2. Save and reopen the reservation.

**Expected result:**
The reservation is saved on the exact date selected (the following month's 3rd), not rolled over into the month after.

**Actual result:**
Date now resolves correctly via a single `Date` constructor call instead of sequential setters. See DR-05.

---

**ID: TC-06** – Verify a logged-in customer can submit feedback on a Finished reservation
**Status:** Failed (partially) – re-test after next redeploy
**Covers:** US04

**Pre-conditions:**
Customer has a Finished reservation with an order attached; customer has a valid session.

**Steps:**
1. Open "Leave Feedback", rate the service, and submit.

**Expected result:**
`POST /feedback` returns `200`/`201`; no "Unauthorized" or "Forbidden" toast.

**Actual result:**
Verified live: the original `401 Unauthorized` is gone (Cognito authorizer now attached to `POST /feedback`). But the same live call now returns `403 "Only customers can create feedback."` for an actual customer account – a second, previously-hidden bug in `UserService.GetUserRoleAsync`. Fixed in code (not yet redeployed). See DR-06. Re-run this test after the next backend redeploy before marking Passed.

---

**ID: TC-07** – Verify every reservation on the waiter dashboard resolves its customer name and order summary under realistic list size
**Status:** Passed
**Covers:** US05

**Pre-conditions:**
Waiter has 20+ assigned reservations.

**Steps:**
1. Sign in as the waiter and open the dashboard.
2. Wait for the list to finish loading.

**Expected result:**
No row shows "Unknown customer" or "0 dishes" due to a failed background lookup; the browser console shows no CORS-masked `status: 0` errors for `/orders/{id}` or `/users/{id}/name`.

**Actual result:**
Enrichment calls are now capped at concurrency 2 and all rows resolve correctly. See DR-07. (Known residual risk: the same unbounded-fan-out pattern is not yet fixed on the customer-facing `/reservations` page — see Known Issues in `defect-reports.md`.)
