# Avzar roadmap work log — Pass 4

## Implemented in source
- Worker self-service leave requests (`Pending` state).
- Manager-only pending leave list and approve/reject workflow.
- Approved requests are checked against overlapping approved leave before approval; only approved leave affects scheduler constraints.
- Review actor, timestamp, and optional note persisted with migration `20261017120000_AddWorkerLeaveReview`.
- Worker schedule UI includes request and review controls.

## Verification limits
- Archive and source structure can be checked locally, but C# compilation, EF migration execution, PostgreSQL integration tests, and web typecheck/tests require dependencies not present in this environment.
- Do not mark the leave workflow production-verified until CI builds the backend and web project, applies migrations to PostgreSQL, and exercises request/approve/reject plus cross-business denial.
