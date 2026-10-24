# Issue triage

Use a few labels to help readers find the right discussion. A label describes the request; it does not promise
that someone is working on it.

| Label | Use |
|---|---|
| `bug` | Something does not work as expected |
| `enhancement` | A proposed feature or improvement |
| `documentation` | Documentation or help text |
| `question` | A usage or setup question |
| `maintenance` | Repository maintenance or project infrastructure |
| `dependencies` | Dependency updates |
| `on hold` | Paused by the maintainer; do not take it |

An issue may have more than one label. The bug and improvement forms already apply their corresponding label;
the maintainer can refine the labels after reading a report. Other existing labels remain available when useful.

## Backlog and releases

The **Backlog** milestone contains unscheduled improvements. It has no due date and does not commit an issue to
a release. An issue moves to a version milestone only when the maintainer chooses that release's scope.

Keep paused issues labelled `on hold` until the maintainer resumes them. Do not assign them to a release just to
empty the backlog. Check the issue discussion and [`FEATURES.md`](FEATURES.md) before taking a task, then follow
[`CONTRIBUTING.md`](../CONTRIBUTING.md).

## Reviewing a new report

1. Check for an existing issue and ask for any missing reproduction details.
2. Apply the labels that describe the report. Use `question` for help requests rather than marking them as bugs.
3. Keep credentials and environment-specific information out of public reports. For vulnerabilities, direct the
   reporter to the private channel in [`SECURITY.md`](../SECURITY.md).
4. Record a decision in the issue. Closing a duplicate should link to the issue that tracks the work.
