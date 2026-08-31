# AI usage disclosure

## Tool and scope

OpenAI Codex was used as a coding assistant on 2026-08-31. The assessment explicitly permitted AI use. Codex helped extract requirements from the supplied PDF, propose a scoped architecture, create code and documentation, run local commands, and diagnose test failures.

## Prompt log

The initiating user prompt, reproduced without recruiter contact details, was:

> I have an assessment to do for a senior developer position at Sygnia. We have been given permission to use AI tools and I need to ace this assessment. Let's do the assessment first locally and test it fully. Then the next step will be to place it in my GitHub repo. The last step will be to create a video.

Attachment supplied with the prompt: `Senior_Developer_Assignment.pdf`, describing the Trade Ingestion and Portfolio Snapshot Service requirements.

The work was completed in one interactive Codex session rather than through separate copy-pasted code-generation prompts. The effective follow-up tasks given to the assistant were:

1. Extract the exact required and optional deliverables from the PDF and treat them as assessment requirements.
2. Implement the required .NET 8, EF Core, and SQL Server solution locally before any GitHub publication.
3. Use immutable event versions for duplicate/correction behavior and document the trade-offs.
4. Add a parameterized, set-based SQL artifact with as-of correction handling and supporting indexes.
5. Add operational logging, correlation IDs, health checks, problem responses, and a correction rollout switch.
6. Test the complete flow against real SQL Server, including concurrency and the SQL artifact.
7. Produce reviewer-facing run instructions, design notes, a demo script, and this AI disclosure.

## Human judgment and validation

AI output was not accepted as proof of correctness. The implementation was compiled with warnings treated as errors and strict analyzers. EF migrations were applied to SQL Server LocalDB, seed data and the stored procedure were inspected with `sqlcmd`, and the integration suite exercised the hosted API and real SQL Server behavior.

The test run caught two issues in the first draft: locale-sensitive decimal validation and non-idempotent test cleanup. Both were corrected before the final verification. The architecture, accounting assumptions, omissions, and rollout behavior are documented in `SOLUTION.md` so they can be challenged during the technical interview.
