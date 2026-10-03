# GigLedger Slice 4: what the road actually does

**Type:** Regular Sprint · **Opened:** 2026-10-02 · **Status:** CLOSED 2026-10-03

Raised during a walkthrough with a working Spark driver, who will test it.

## Goal

Record the parts of a shift that today leave no trace: offers turned down, trips that got cancelled,
tips that change after the fact, and numbers that cannot be real.

## Order of work (SDD section 1, unchanged)

For each story: the requirement goes into the SRS first, then the tests are written from it and seen
failing, then the code, then the screen. Every acceptance criterion below is checkable in the session
it is worked in.

## Stories

| # | Story | Done when |
|---|---|---|
| 1 | **Decline an offer, with reasons.** An evaluated offer can be declined. The decline is stored with the offer's numbers, one or more reasons from a fixed list, an optional note, and the time. Never deleted. | SRS requirement added; tests first; the Offer page has a Decline action; declines appear in a report by reason. |
| 2 | **Implausible values.** Two levels. *Impossible* values are refused with a sentence saying why (for example: implied speed over a set limit, one charge larger than the battery, a start time in the future, a shift over 24 hours). *Implausible* values are allowed only after the driver confirms, and the record is marked confirmed. | SRS requirement with the limits stated as data, not code; tests for each limit; the screens show the refusal or the confirm step. |
| 3 | **Tip tracking, optional (rescoped 2026-10-03).** Pay is the total shown at accept, tip included; a recorded tip is never added on top (reverses 0.2). Setting, off by default: the promised tip at accept, posted tips summed against the trip (no per-order breakdown), *tip pending* until the driver marks *all tips in*, then gross = base + posted and the adjustment is shown. | SRS requirement; tests first; a setting; the accept field and the all-tips-in action on screen. |
| 4 | **Cancelled trips.** A trip can end cancelled: who cancelled (customer, store, platform, driver), when (before pickup, after pickup, at the door), what it paid (full, partial, none), **why, from a fixed list of reasons plus an optional note** (added by the Architect 2026-10-03), and the miles and minutes actually driven, including any return to the store. Those still count against the shift. | SRS requirement; tests; a Cancel action on an open trip; the shift summary includes cancelled trips' cost; a cancellation report. |

## Open questions

0. **Story 1 reverses a recorded decision.** SRS 0.2 says it "closed the four open questions: ... no
   declined offers ...". Declining with reasons is the newer call; the SRS change must state that it
   reverses 0.2's decision and why, not only add a requirement.

1. ~~**The decline reasons.**~~ **Answered 2026-10-03 (the Architect), in his order of consideration,
   which is also the display order:** pay too low · too far (includes bad geometry) · too many items ·
   pharmacy · alcohol · heavy · stairs · apartment · too many drops (batched orders) · low charge ·
   ending shift · other (with a note). Dropped from the draft: "bad area or road", which is
   rare for him and goes under "other" with a note; if testers use it often, the decline report will
   show it in the notes.
2. ~~**Does Spark's earnings workbook list tips per drop, or only per trip?**~~ **Answered 2026-10-03
   (the Architect): both, at different times.** The offer shows one tip for the whole trip; at payout
   the tip arrives split, two or three entries, each against its own order. So story 3 records the
   promised total at accept and each posted tip against its order; the trip's adjustment is computed
   once every order has posted. **Tips attach to orders, not drops** (the Architect, same day): Spark
   batches exist partly to get non-tipping orders delivered by packaging them with tipping ones. So a
   batch's orders carry their own posted tips, a posted tip of 0 is a real, known zero (not the
   UNKNOWN null), and the adjustment report can show how often a batch carried a zero-tip order.
3. ~~**The implausible limits:**~~ **Answered 2026-10-03, all five.** The numbers for each (speed, trip length, pay, tip, shift length).
   Purpose (stated 2026-10-03): catch entry errors at the moment of entry; a mistyped miles or minutes
   figure inflates the mileage deduction or wrecks $/hr. Answers so far (the Architect, 2026-10-03):

   **Model changed (the Architect, 2026-10-03):** the upper level is **not a refusal**. Above it, the
   value requires a reasonable explanation and authorization; if explained, it is recorded and allowed.
   His reason, on speed: "maybe there is a reason the calculation shows the anomaly." **Who authorizes:
   the user, for their own entries** (the Architect: the tester is "just any user"; there are no roles).
   Authorization = a written explanation plus a deliberate second confirm.
   **Every explained value stays flagged and appears on an "explained values" report with its
   explanation**, for review before filing. His rule: "anything above the ridiculous threshold requires
   documentation to be used."

   **Refused outright, with a sentence saying why** (the Architect: "they can't be true"): a charge
   larger than the vehicle's battery (needs the battery size stored per vehicle), a start time in the
   future, a shift over 24 hours, and negative pay, miles or minutes.

   | Limit | Confirm above | Explain and authorize above |
   |---|---|---|
   | Speed (trip miles over trip minutes, stops included) | 60 mph | 90 mph |
   | Trip length (store to last drop, return included) | 50 mi | 150 mi |
   | Pay per trip (base, before tip; negative refused) | $80 | $250 |
   | Tip per trip (promised total; negative refused) | $40 | $150 |
   | Shift length (over 24 hours refused) | 12 h | 16 h |

4. ~~**The cancel reasons**~~ **Answered 2026-10-03.** **His list, in order:** order not ready · unreachable ·
   wrong address · customer issue (for example dogs, attitude) · emergency · car problems · other (with a note). **"Customer cancelled" is not a reason:**
   when the customer cancels, no reason is asked (the who-cancelled field carries it). **A reason is asked only
   when the driver cancels** (the Architect). "Changed my mind" considered and ruled out as a reason.

## Already works (checked 2026-10-02, not a story)

Evaluating an offer without a shift: the Offer page evaluates with no shift open and says *"No shift
is open. Start a shift to accept an offer."* (`OfferPage.razor`).

## Retro

At close: what this slice did badly.

Written at close, 2026-10-03:

**What landed.** Declines with reasons; accept starting a shift; entry checks at three levels with an
explained-values report; pay as the total shown, with optional tip tracking that can be edited after
accept; cancelled trips; a one-drop trip's return defaulting to its route. Suite 263 to 425, every new test
written first and seen failing, every test that passed early broken on purpose to prove it could fail.

**What the slice did badly.**
- **The biggest defect was in the requirements, not the code.** SRS 0.2 said a tip is added to pay; the
  driver enters pay as the total shown, tip included. Every tip would have counted twice. It surfaced only
  because a question about the accept screen got asked; nothing checked the SRS against how data is
  actually entered. GigLedger is not yet his live record, so no stored data was affected.
- **Built from the story text, then dropped.** The tip-tracking on/off setting came from the story's word
  "opt-in"; the driver never asked for it, and it was removed in the same story (its column stays). Ask
  what a setting is for before building one.
- **Every live check found something the tests could not:** a scare from launching the app in production
  mode (the error bar shows permanently); the Items box starting at 0; an 8,000-minute trip passing every
  check; the tip display jammed into the trip row. The live check is part of done, not a courtesy.
- **Existing tests had encoded the very errors story 2 now stops** (243 kWh for 24.3, 5 minutes for 55),
  and seven encoded the double-counting tip rule. Tests prove the rule they were written from, right or
  wrong.
- **Process:** one commit (removing cash tips) was pushed without asking, and the removal was itself a
  misreading, corrected in the next commit; a test helper started a second shift; long shell scripts
  failed to parse twice, costing calls the driver reads as noise.

**What worked.** Tests first with planted violations; the Architect's answers recorded in his words as
they came; scope cut on his word (cash tips: not now, with his reason on record).

## Log

- 2026-10-02: Opened on the Architect's word, four stories.
- 2026-10-03: All three open questions answered by the Architect (S20261003-1): decline reasons and their
  order; tips attach to orders; the limits, with a three-level model (confirm, document, refuse only the
  impossible) and an explained-values report.
- 2026-10-03: Question 4 added and answered (cancel reasons, asked only on a driver cancel).
- 2026-10-03: **Story 1 built, awaiting the live check and acceptance.** SRS 0.6: FR-2 reversal note,
  FR-2a declines with reasons (a decline needs an open shift), FR-2b accept starts a shift (added by
  the Architect), FR-21a decline report. SDD 0.7 section 6.9. 38 tests written first: 35 failed, the 3
  that passed early (reason order, no decline without a shift, nothing held means nothing accepted)
  were each shown failing on a planted violation. Suite 300 passed. An existing test caught Declines
  missing from the full export (FR-32); fixed. Removed `UI_Offer_WithoutAnOpenShiftThereIsNothingToAcceptInto`,
  which asserted the behaviour FR-2b reverses.
- 2026-10-03: **Story 1 ACCEPTED (the Architect)** after a live check on a scratch database: accept with no
  shift started the shift and recorded the trip at the same moment; a decline stored two reasons in order;
  "Other" with no note was refused; the Reports declines table matched. Found in the check and fixed (tests
  first): the Items box started at 0; it now starts blank and is asked for, never taken as 0. Drops keeps
  its default of 1 (his). Also found: launched without a development environment, the app does not serve
  its scoped stylesheet, so the error bar shows permanently; a launch fault, not a code fault.
- 2026-10-03: **Story 2 built, awaiting the live check and acceptance.** The Architect's answers: pay is
  checked only on the offer; speed and trip length on actuals, tip on the tip, shift length at the end;
  a charge is refused above the battery size plus 25%. His yes on two calls: 5 minutes of grace on a
  future start; the report lists explained values only, for now. SRS 0.7 section 5.9 (FR-35 to FR-38),
  SDD 0.8 section 6.10. 54 tests written first: 52 failed, the 2 that passed early were shown failing on
  planted violations. Five existing tests broke on the new rules; each kept its purpose: two modelled the
  very typos the checks now stop at entry (243 kWh for 24.3; 5 minutes for 55), so their typos became ones
  the checks let through; the decimal-precision test records its large pay with an explanation; a tax
  test's clock was earlier than its own shift; the export now includes Limits and EntryMarks. Suite 356
  passed. Deferred: marks shown beside each record on screen.
- 2026-10-03: **Story 2 ACCEPTED (the Architect)** after a live check: a $500 pay, a 1,200-mile trip, and a
  $1,000 tip each went through the explanation step and were marked; nothing stored without one. Found in
  the check: a trip of 8,000 minutes passed, since over 1,200 miles that is only 9 mph and no limit covered
  trip time. His call: refuse a trip over 24 hours (FR-37). Test first, seen failing.
- 2026-10-03: **Story 3 rescoped (the Architect).** The accept screen shows one total; the tip is visible
  by opening the offer; he enters the total as pay. So a recorded tip added on top would count twice
  (GigLedger is not yet his live record, so no stored data is affected). His aim: tips tracked for tax,
  not who tipped. Dropped: tips per order, the zero-tip-order count. Kept optional, off by default.
- 2026-10-03: **Story 3 built, awaiting the live check and acceptance.** His answers: late tips accepted and
  recomputed; a customer can change a tip for 24 hours, so *all tips in* is hidden until 24 hours after the
  trip ended; a lowered tip just posts smaller (negative tips stay refused). SRS 0.8 (FR-1, FR-6 reversed,
  FR-6a, FR-17, FR-31, FR-36), SDD 0.9 section 6.11. 31 tests written first: 29 failed, the 2 that passed
  early were shown failing on planted violations. Seven existing tests asserted the reversed rule (a tip
  added on top of pay, one tip per trip, tips counted on their own when untracked); each was converted to
  the new rule, keeping its purpose (the ones about tips being counted now use a tracked trip with all
  tips in). EF generated an UPDATE on the seeded settings row; the append-only triggers refuse it, so it
  was removed by hand, as in RecordKeeping. Suite 388 passed.
- 2026-10-03: **Story 3 changed during its live check (the Architect).** The tip display was jammed into
  the trip row; it now has its own line. The tip field is always on the accept screen (the total tip):
  the Track tips setting is dropped (its column stays, unused; the migration is published). Every tip
  field is optional; unused, the whole pay counts as the fee. The promised tip can be added after accept
  (no reason while the shift is open) or changed (reason required), and adding it after the shift closed
  needs a reason too. SRS 0.9, SDD 0.10. 13 tests first, all failed; suite 400 passed.
- 2026-10-03: **Story 3 ACCEPTED (the Architect)** after the live check and the changes above.
- 2026-10-03: **Cash tips: not built now; a future enhancement.** The Architect's reason, in his words:
  the decision rests on *"the insignificance of cash tipping in this game, not to avoid taxes."* Cash tips
  are rare in delivery driving. YAGNI for this slice; *"it will probably be implemented in some form at
  some time."*
- 2026-10-03: **Story 4 built, awaiting the live check and acceptance.** His answers: not shopped pays nothing,
  0 miles, and its time is the shift's unpaid time; shopped pays the pay minus the tip (the cancel asks for
  the tip if none was entered), its minutes are trip time, its miles start from the offer's (the mileage
  adjustment shown); the cancel time is captured; a tip posted on a cancel adds to what it paid; return
  miles are their own field; a cancel is final. Also his: a one-drop trip's return always defaults to its
  route (FR-3). SRS 0.10, SDD 0.11 section 6.12. 25 tests written first: 23 failed, the 2 that passed early
  were shown failing on planted violations (one test helper bug fixed: it started a second shift). Suite 425.
- 2026-10-03: **Story 4 ACCEPTED (the Architect)** after a live check: a shopped driver cancel paid $26.89 with
  the tip given at cancel; a store cancel before pickup paid $0; a two-drop trip asked for its return.
  **SLICE 4 CLOSED** on his word, retro written.
