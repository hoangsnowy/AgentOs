# KC Dataset — benchmark scenarios

5 dataset files for the experimental scenarios KC1-KC5. Each file = an array of test cases `{id, input, expected}`.

| File | KC | Endpoint run | Metrics measured |
|---|---|---|---|
| `kc1.json` | KC1 — Requirement Analysis | `POST /requirement` | tokens, cost, entitiesCount, endpointsCount, acceptanceCriteriaCount |
| `kc2.json` | KC2 — Code Generation | `POST /code` | tokens, cost, filesCount, must-have class/route |
| `kc3.json` | KC3 — Test Generation | `POST /test` | tokens, cost, happy/edge/error count, framework, coverage |
| `kc4.json` | KC4 — Pipeline End-to-End | `POST /pipeline` | total tokens/cost, iteration count, final status, qa score |
| `kc5.json` | KC5 — QA Consistency | `POST /qa` | tokens, cost, score, isConsistent, issues count |

## How to run

Use the `/kc-bench` skill:

```
/kc-bench all              # all 5 KC scenarios
/kc-bench KC4              # only KC4 (pipeline)
/kc-bench KC1,KC4 --real   # run live with Claude+Azure (cost warning)
```

The skill automatically:
1. Starts the API locally if it is not already running.
2. Loops over each case × N iterations (default 3).
3. POSTs the request, measures metrics.
4. Aggregates → `docs/bench/kc-summary-{date}.xlsx` + markdown.

## Fixture dependencies (kc2/kc3/kc5)

KC2/KC3/KC5 need output from a previous KC (e.g. KC2 needs the `RequirementSpec` from KC1).
The `specFixture`, `codeFixture`, `testsFixture` fields point to a recorded snapshot fixture
(record real LLM responses to refresh these stub files).

If a fixture does not yet exist, the bench skill automatically skips the case and reports the reason.

## Extending

Add a new case: append to the JSON, set a unique `id` (e.g. `KC1-004`).
Do NOT change the `expected` shape without updating the bench harness in parallel.
