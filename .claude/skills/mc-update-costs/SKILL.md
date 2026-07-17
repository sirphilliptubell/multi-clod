---
name: mc-update-costs
description: Refreshes MultiClod's Claude model pricing table (src/MultiClod.App/Costs/ClaudeModelPricing.cs) from Anthropic's live pricing/model docs. Use when asked to update Claude pricing, add a new model's rates, or check whether cost tracking is using current prices.
---

# mc-update-costs

## Goal

Keep `src/MultiClod.App/Costs/ClaudeModelPricing.cs` in sync with Anthropic's published rates,
without ever destroying pricing history. The dictionary is append-only: a price change adds a new
dated entry, it never edits or deletes an existing one.

## Steps

1. WebFetch `https://platform.claude.com/docs/en/about-claude/pricing.md` — has $/million-token
   rates (base input, 5-minute cache write, 1-hour cache write, cache read, output) per **display
   name**, no model ID slugs.
2. WebFetch `https://platform.claude.com/docs/en/about-claude/models/overview` — has model **ID
   slugs** per display name (a current-models table plus a "Legacy models" section), and sometimes
   prices too.
3. Cross-reference the two pages by display name to build a slug -> 5-rate mapping for every model
   currently listed live. A display name can map to more than one slug (an alias plus a dated
   snapshot ID, e.g. `claude-haiku-4-5` and `claude-haiku-4-5-20251001`) — every slug gets its own
   entry with identical rates; don't try to group or dedupe them.
4. Read the current `Entries` list in `src/MultiClod.App/Costs/ClaudeModelPricing.cs`.
5. For each (slug, rate-set) found live, compare against what's already in `Entries`:
   - **No entry exists yet for that slug at all** -> append a new `ClaudeModelRateEntry`.
     `EffectiveFromUtc` is the page's explicitly stated effective date if it gives one, otherwise
     `DateTimeOffset.MinValue` (matches the existing convention for "always been this price").
     `EffectiveUntilUtc = null` (open-ended).
   - **An existing open-ended entry for that slug (`EffectiveUntilUtc == null`) has different
     rates than the live page now shows** -> this is a price change. Append a *new* entry starting
     at the page's stated effective date (or today, if the page doesn't give one), with
     `EffectiveUntilUtc = null`. Then edit the *previous* entry's `EffectiveUntilUtc` to that exact
     same start date, so the two ranges are adjacent with no gap and no overlap. This is the only
     kind of edit ever made to an existing entry — the numeric rates on it are never touched.
   - **Rates match exactly** -> leave that entry alone.
6. **Never** delete an entry, and never edit the numeric rates on a historical (closed-end) entry —
   only its `EffectiveUntilUtc` may be set once, at the moment a newer entry supersedes it.
   **Never** remove a slug that's disappeared from the live pages entirely — a fully retired model
   simply keeps its last (open-ended) entry forever.
7. Build the project (`dotnet build` on the solution, or at least the `MultiClod.App` project) to
   confirm the change compiles.
8. Summarize exactly what was appended and what was closed off, in plain English, before
   finishing. If nothing changed (the live pages match what's already in the file), say so plainly
   instead of claiming an update happened.

## Reference example

`claude-sonnet-5` already has two adjacent entries in the file — an introductory-pricing entry
effective until 2026-09-01, and a standard-pricing entry effective from that date onward. Any
future price change for any model should follow this exact same two-entry, adjacent-dates shape.
