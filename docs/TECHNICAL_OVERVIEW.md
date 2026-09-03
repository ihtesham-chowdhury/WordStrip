# WordStrip — Technical Overview

> **Purpose of this document.** WordStrip is a working Windows utility built by one developer. This
> describes what it does, how it does it, and what is measurably true about it, so that a reader with no
> access to the source can give useful architectural and algorithmic criticism.
>
> **What I want from a reviewer is in §11.** Everything before that is context.
>
> Numbers here are measured on the development machine unless marked as an estimate. Where something is an
> assumption rather than a measurement, it says so. Nothing has been rounded in a flattering direction.

---

## 1. What it is

WordStrip adds phone-keyboard-style word suggestions and autocorrect to the **physical** Windows keyboard.

It runs in the system tray with no main window. As you type in any application, a small floating strip
appears near the caret showing ranked candidate words. `Tab` cycles them, `Space` inserts the highlighted
one, `Esc` dismisses. When you finish a word, conservative autocorrect may fix an obvious misspelling.
Between words the strip stays up and predicts what comes next.

The motivating observation is simple: phones have had a suggestion row for fifteen years, and Windows has
nothing equivalent for a hardware keyboard.

**Everything runs locally.** No network calls at runtime, no telemetry, no account. The one exception is an
optional model download the user explicitly initiates.

| | |
|---|---|
| Platform | Windows 10 1809+ / Windows 11, x64 |
| Language | C# 12 on .NET 8, plus a small native C++ component |
| UI | WPF (WinForms only for the tray icon) |
| Size | ~70 MB installer, self-contained (no .NET install needed) |
| Tests | 362 unit tests, plus an end-to-end harness driving real Win32 controls |
| Status | Working; used daily by its author and a handful of testers. Not publicly released. |

---

## 2. What works today

- Prefix completion and fuzzy correction from a 60,000-word English dictionary
- Context-aware next-word prediction from a trigram model
- Multi-word phrase suggestions ("let me know", "looking forward to")
- Emoji suggestions on unambiguous keyword matches
- A personal dictionary the user adds words to, protected from autocorrect
- Optional on-device learning from the user's own typing
- Optional neural reranking with a downloaded 82M-parameter language model
- Seven visual themes, spring-based motion, full accessibility fallbacks
- **Works in Chrome, Edge, Brave, Electron apps, Microsoft Word, Notepad and Win32 dialogs**

That last line is recent and was the hardest part; see §5.

---

## 3. Architecture

```
                      keystroke / document change
                                  |
              +-------------------+-------------------+
              |                                       |
     Low-level keyboard hook              TSF text service (native DLL,
     (WH_KEYBOARD_LL, in-process)          loaded into the host application)
              |                                       |
     shadow buffer of the                    reads text before the caret
     word being typed                        out of the real document
              |                                       |
              +-------------------+-------------------+
                                  |
                        ITextContextProvider
                (composite: prefers the richer source,
                 falls back per keystroke, never fails)
                                  |
                          SuggestionController
                                  |
                          PredictionEngine
              +-------+-----------+-----------+--------+
              |       |           |           |        |
         PrefixIndex SymSpell  n-gram LM   Personal   Emoji
              |       |           |         vocab/LM    |
              +-------+-----------+-----------+--------+
                                  |
                        Ranker (banded scoring)
                                  |
                     optional neural reranker (async)
                                  |
                          Suggestion bar (WPF)
```

**Three projects, deliberately separated:**

- `WordStrip.Core` — prediction, input, settings. **Zero NuGet dependencies.** This is enforced, not
  incidental: it is why the prediction stack is predictable and why a broken package cannot take the app
  down.
- `WordStrip.Neural` — ONNX Runtime, isolated behind an interface so Core never sees it.
- `WordStrip.Tip` — the native C++ text service.

---

## 4. The prediction stack in detail

Numbers below are the actual constants in the code.

### 4.1 Candidate generation

**Prefix completion.** A 60,000-word dictionary (SymSpell's `frequency_dictionary_en_82_765`, MIT) held in
an ordinally sorted array. Prefix ranges are found by binary search. **4.6–14.7 µs per lookup** — this
replaced a linear scan and is 600–700× faster.

**Fuzzy matching.** A SymSpell delete-variant index at edit distance 2, with bounded Damerau-Levenshtein for
verification. Index build costs **~2.05 s at startup** on a background thread, and the index holds **28.9 MB** (see §6 — it was 291 MB until 2026-08-13).

**Autocorrect** applies only on a word boundary, only above a frequency-confidence threshold, and never to a
word in the user's personal dictionary. **149.2 µs per call.**

### 4.2 Context

An n-gram model built offline from **7,935,098 tokens** across 61 Project Gutenberg books plus 242,342
SymSpell bigrams:

| | |
|---|---|
| On disk | 7.25 MB (2.42 bigram + 4.83 trigram), text format, committed to the repo |
| Entries | 120,082 bigrams / 227,213 trigrams |
| Contexts | 23,352 bigram / 87,319 trigram |
| Resident | 26.3 MB |
| Load | 1.5–3.4 s, background thread |
| Lookup | 1.6–3.2 µs |

**Scoring is stupid backoff** (Brants et al. 2007), λ = 0.4, trigram → bigram → unigram.

**The model stores probabilities, not counts.** Two sources whose raw counts differ by orders of magnitude
cannot be summed — the larger would erase the smaller. Each is reduced to a conditional distribution and
mixed; where only one source knows a context, it supplies the whole distribution.

### 4.3 Ranking

Deterministic **banded** scoring. Bands are 100 apart:

| Band | Score | Meaning |
|---|---|---|
| ExactWord | 300 | the user has typed this word completely |
| Prefix | 200 | a completion of what they have typed |
| FrequentWord | 100 | a common word offered between words |
| Fuzzy | 0 | a correction candidate |

Bonuses stack **inside** a band and are individually capped so none can cross the 100-point gap:

| Signal | Cap |
|---|---|
| n-gram context | 40 |
| personal dictionary word | 30 |
| neural reranking | 25 |
| learned personal usage | 15 |

Worst case ≈ 85 + ~10 of frequency weighting, deliberately under 100. **A word the user has finished typing
always wins**, however much other evidence argues otherwise.

Two tuning decisions that mattered:

- **Raw frequency is not added on top of conditional probability.** `P(word | context)` already accounts for
  how common a word is; double-counting promotes function words. Weight is 8.0, not the 2.0 first tried —
  at 2.0, "i am" predicted *the* and *to*, and buried *sure*.
- **Context is resolved once per candidate list, not per candidate.** Doing it per candidate cost
  161 µs → 484 µs per keystroke.

### 4.4 Phrases

Bounded beam search over the n-gram model. Beam width 6, branching factor 4, maximum 3 words.

Scored on **mean log probability per word**, so length never wins by itself. Extensions require trigram
evidence; unigram-tier seeds are never extended. Quality floor: mean log probability ≥ −1.4.

**349–548 µs per call.** Unseen context costs 13–52 µs because no expansion is attempted.

### 4.5 Emoji

~300 curated keyword→emoji entries, minimum prefix length 3. At most one emoji, always in the last slot,
**placed by policy rather than scored against words**. Ambiguous prefixes return nothing — `"cal"` matches
both *calendar* and *call*, so it yields neither.

### 4.6 Personal vocabulary and learning

**Vocabulary** (user-added words): 5,000 cap, least-used evicted. Normalised lookup key stored separately
from display casing, which is what keeps "GitHub" from becoming "github". Protected from autocorrect.

**Learning** (opt-in, default off) records counts of words, pairs and triples from committed words — never
text:

| | |
|---|---|
| Bounds | 20,000 entries per order, counts saturate at 1,000 |
| Decay | ×0.9 every 20,000 learned words |
| Cold start | linear confidence ramp to full weight at 2,000 words |
| Growth | 45 KB @ 1k words · 164 KB @ 5k · 193 KB @ 20k · 394 KB @ 100k |

Learning is gated on the same check as suggesting: a field the app cannot positively identify is never
learned from.

### 4.7 Optional neural reranking

DistilGPT2 (82M parameters), int8 ONNX, **227 MB download**, Apache 2.0, CPU only. Never bundled; the user
downloads it explicitly after being shown size, licence and publisher.

It is a **cascade, not a stage**:

- Skipped entirely when the statistical top candidate already has confidence ≥ 0.62
- Runs asynchronously with a 250 ms timeout — never blocks a keystroke
- Results arriving after the context moved on are discarded by sequence number
- Can only reorder; **it cannot introduce a word the statistical stack did not already offer**

| | |
|---|---|
| Cold load | 3.0–4.4 s, background thread |
| Warm inference | 54–80 ms, one forward pass scores the whole candidate list |
| Allocation | ~2.2 MB per call |

**Honest quality note.** First-token scoring on an 82M model quantised to int8 is a useful but coarse
signal. It reliably rejects nonsense and clearly reads context — it scores "you" about 7.5 nats higher after
"thank" than after "looking". It does **not** reliably make finer calls: after "i am really looking" it
narrowly prefers "you" to "forward". The tests assert what it can actually do, not what would be nice.

---

## 5. Reaching applications that don't use Win32 controls

This was the hardest problem and is the most recent work.

**The original mechanism** was a global low-level keyboard hook maintaining a shadow buffer of the word being
typed, with text inserted by posting window messages to the focused control. That works in classic Win32
`Edit`/`RichEdit` controls — Notepad, dialogs, older desktop apps — and **nowhere else**. Chrome, Edge,
Electron and Office all draw their own text and expose no such control.

**The fix** is a Windows Text Services Framework text service: a native COM DLL that Windows loads into
every application accepting text. It reads the text before the caret out of the real document and sends it
to the tray process over a named pipe.

Findings worth passing on, since documentation on this is thin:

- **Every TIP on a stock Windows 11 machine is native and machine-registered.** Of 21 registered text
  services, 11 have COM servers and **none is managed** — no `mscoree`, no `comhost`. All 21 register under
  `HKLM`, none under `HKCU`, so registration requires administrator rights.
- **All of them ship both x64 and 32-bit servers.** A 32-bit host loads the 32-bit TIP; there is no
  thunking. WordStrip ships x64 only, so 32-bit applications fall back to the keyboard hook.
- **Link the CRT statically.** A dynamically linked TIP injects a dependency on one specific VC++
  redistributable into every application on the machine, and hosts without it fail to load it silently.
- **The pipe write is capped at 20 ms and abandoned on timeout.** It happens on the host application's UI
  thread; a blocking write is a frozen Chrome.
- **Password fields are suppressed via `GUID_COMPARTMENT_KEYBOARD_DISABLED`.** TSF has no explicit password
  flag. This was recorded as an unverified assumption and then confirmed by testing.

**Confirmed working by hand:** Chrome, Brave, Edge, Electron (Claude desktop), Microsoft Word, Notepad.

**Not yet done:** committing text *through* TSF. Insertion still uses the older mechanism, so **autocorrect
and personal learning do not run in browsers** — only prediction does.

### The fallback is a first-class component

Providers are selected **per call**, not per session, because TSF availability follows the focused
application and alt-tabbing raises no event. A provider that throws is taken out of service and the next one
answers. If all fail, the answer is "nothing is known here" — which the stack already handles, because it is
what focus on a button looks like.

This was built **before** the TSF provider existed, deliberately. A fallback added afterwards around
something already known to be flaky is a fallback nobody trusts.

---

## 6. Measured performance

Per keystroke, against the real dictionary and model:

| Operation | Cost |
|---|---|
| Prefix lookup | 4.6–14.7 µs |
| n-gram lookup | 1.6–3.2 µs |
| Next word, end to end | 30.9–75.3 µs |
| Completion with context | 158–330 µs |
| Phrase generation | 349–548 µs |
| Autocorrection | 274.5 µs |
| Neural rerank (when not skipped) | 54–80 ms, async |

Startup: dictionary 185 ms · SymSpell index ~6.2 s · n-gram model 1.5–3.4 s · neural model 3.0–4.4 s. All on
background threads; the tray icon appears immediately.

**Memory**, measured 2026-08-13 after the fix described below: **222 MB private with no neural model, 749 MB
with it.**

It was 524 MB / 1.1 GB until the SymSpell index was rebuilt. The assumption that the index was the bulk
turned out to be correct and understated — it measured **291 MB**, ninety per cent of the prediction stack,
stored as `Dictionary<string, List<string>>` over 1.8 million delete variants. The largest cost was not the
strings but the 1.8 million `List` objects, each with its own backing array, holding on average barely more
than one entry.

Replaced with three flat arrays and a binary search: variants as 64-bit hashes, postings as word indices
packed end to end with an offset table. **291 MB → 28.9 MB**, and *faster*: index build 6.2 s → 2.05 s,
autocorrection 274.5 µs → 149.2 µs.

Hashing the key is sound here only because every candidate is verified with bounded Damerau-Levenshtein
afterwards, so a collision costs one wasted comparison and can never produce a wrong suggestion.

---

## 7. Privacy model

- No network calls at runtime. The only outbound request is the user-initiated model download.
- No telemetry, no analytics, no account, no identifiers.
- Suggestions, autocorrect and learning are all disabled in password fields.
- By default **nothing typed is written to disk at all.** With learning on, what is written is counts of
  words, pairs and triples — never text.
- The TSF wire format **caps transmitted text at 128 characters and has nowhere to put more.** A service
  with an entire document available still cannot send it. This is a format constraint rather than a promise.
- Everything lives in one folder as plain files the user can read and delete.

---

## 8. Known limitations

1. **English only.** One bundled dictionary.
2. **The corpus skews literary.** 19th- and early-20th-century novels, so ordinary English is well covered
   and modern, technical or workplace phrasing is not. The model has never seen "pull request". Phrases
   inherit the same register — grammatical, but dated.
3. **Autocorrect and learning do not work in browsers** — see §5.
4. **Autocorrect cannot correct *into* a personal word.** The fuzzy index is built at startup from the
   general dictionary and never rebuilt, so a misspelling of a user-added word is not corrected.
5. **Tab belongs to the bar whenever the bar is visible.** It therefore does not indent or move between
   dialog fields while the strip is up, which with the persistent bar on is most of the time in a text
   field. Esc releases it. This reversed an earlier opposite decision.
6. **32-bit applications never load the text service** and fall back to the keyboard hook.
7. **A keystroke can be missed when the UI thread is briefly busy.** A low-level hook that cannot be
   serviced is timed out by Windows: the app never sees the key, the target application does, and the shadow
   buffer ends up one character short. Reproduces roughly **1 run in 4–6** in an automated harness typing
   faster than a human. Mitigated twice, never fixed.
8. **Unsigned binaries** — SmartScreen warns on first run.
9. **Memory** — see §6 and §11.

---

## 9. Engineering approach

Offered because it shapes what advice is useful.

- **Comments explain why, not what.** Several non-obvious decisions are load-bearing and would look like
  harmless code to a future reader.
- **Additive change.** New signals arrive as new rankers wrapping old ones, never as edits to working
  scoring code.
- **Seams for testability.** Anything reading live Win32 state through a static gets an interface so it can
  be faked.
- **Measured, not assumed.** Performance claims come from a benchmark suite that runs with the tests.
- **Honest documentation.** Failed approaches are recorded alongside working ones, with the reason they
  failed. Several bugs took three or four attempts, and the wrong attempts are written down.

---

## 10. Things that were tried and rejected

Included so a reviewer does not suggest them:

- **Insertion via `SendInput`** — worked, but a long replacement took 80–233 ms and got mangled by hosts
  under load. Now uses window messages where possible: 233 ms → 5.9 ms.
- **Deletion by selecting a range** (`EM_SETSEL`) — a *sent* message jumps ahead of *queued* input, so
  autocorrect read a stale caret and corrupted text roughly one time in three.
- **A single backspace encoding for all controls** — `WM_CHAR 0x08` works on `Edit` and is ignored by
  RichEdit; `WM_KEYDOWN VK_BACK` works on RichEdit and loses characters on `Edit`. Both shipped; both were
  wrong. It now branches on control class.
- **DWM Mica/Acrylic backdrop** — impossible on a per-pixel-alpha layered window.
- **A managed TIP** — ruled out on evidence (§5), not reputation.
- **Trimming the published binary** — WPF's reflection does not survive the trimmer reliably.

---

## 11. Where I would most value criticism

Ordered by how much I think a good answer is worth.

### 11.1 Prediction quality — the corpus problem

This is the big one. **The language model is trained on Victorian novels** because that is what is freely
available in bulk with clean licensing. It produces grammatical, well-formed, and subtly antique
suggestions. After "thank you" it may offer "sir said the".

- What corpora would a serious product use for **modern conversational and workplace English**, with
  licensing that permits redistribution in a closed-source desktop app?
- Is domain adaptation from a general corpus plus a small in-domain one worth it at this scale?
- Would a larger, better-curated corpus beat the neural reranker for a fraction of the runtime cost?

### 11.2 Is the ranking architecture right?

Banded scoring with capped additive bonuses is transparent, deterministic and easy to reason about. It is
also hand-tuned, and every new signal requires re-checking that the caps still sum below the band gap.

- At what point does a learned ranker (logistic regression, GBDT over the same features) beat hand-tuned
  additive bonuses — and how would one gather training data for it **without collecting user text**?
- Are bands the right structure at all, or an early decision now being worked around?

### 11.3 Better use of the neural model

Currently: rerank an existing candidate list by first-token score, capped at +25, skipped when confident.

- Is first-token scoring the right use of a small causal LM here, or is there a better formulation for
  candidate reranking?
- Would a small model **fine-tuned for this task** beat a general 82M model at the same size?
- Is 82M the right size, given a 250 ms budget on CPU and a 227 MB download?

### 11.4 Memory — largely answered, 2026-08-13

**Resolved, and left here because the answer may still be improvable.** The SymSpell index measured 291 MB
and is now 28.9 MB; the app is 222 MB private without the neural model, down from 524 MB. The fix was flat
arrays and a binary search rather than `Dictionary<string, List<string>>` — details in §6.

Remaining questions, for anyone who wants to push further:

- The index is now 1.8M 64-bit hashes plus postings. **An FST or DAWG over the dictionary would likely reach
  single-digit megabytes.** Is the extra complexity worth 20 MB, given the lookup is currently 166 µs and an
  FST would be slower?
- Would it be better to persist the built index to disk at install time and memory-map it, rather than
  rebuild it in 2 seconds on every launch?
- ~160 MB remains that is neither the prediction stack nor the neural model — CLR, WPF, native. Is that
  ordinary for a WPF tray application, or is something wrong?

### 11.5 The dropped-keystroke bug

A low-level hook missing a keystroke while the UI thread is busy (§8 item 7) has been mitigated twice and
never fixed. The obvious real fix is to verify the text before the caret against the shadow buffer before
replacing, and correct the deletion count when they disagree.

**TSF now makes that possible for the first time** in applications where it is available. Is there a better
approach, and what is the right answer for applications where it is not?

### 11.6 What is missing from the product

Honest question rather than rhetorical. Given the feature set in §2 — what would a reviewer expect a
world-class version of this to do that it does not?

---

## 12. Deliberate non-goals

So criticism can be aimed usefully:

- **No cloud inference.** Local-only is the point, not a limitation to be argued out of.
- **No telemetry**, including anonymous usage statistics.
- **No storing user text.** Counts only, and only with consent.
- **Not a full IME.** No composition, no candidate windows in the CJK sense.
- **Not cross-platform.** Windows-specific by design.
