# dbt introduction

[Versión en español](README.es.md)

A small local project to learn what dbt is and how it works. It uses data from a fictional shop.

## What dbt does

dbt (data build tool) transforms data that is **already inside** a database. You write `select` queries, and dbt creates the views and tables in the right order. It also tests the data and builds documentation.

dbt does not extract data from other systems, and it does not schedule jobs.

| Concept | Meaning | Example in this project |
|---|---|---|
| Seed | A small CSV file that dbt loads into a table | [seeds/raw_orders.csv](seeds/raw_orders.csv) |
| Model | One `.sql` file with one `select`. It becomes a view or a table | [stg_orders.sql](models/staging/stg_orders.sql) |
| `ref()` | A reference to another model or seed. dbt uses it to find the order | [dim_customers.sql](models/marts/dim_customers.sql) |
| Materialization | How dbt saves a model: view, table or incremental | [dbt_project.yml](dbt_project.yml), [fct_orders.sql](models/marts/fct_orders.sql) |
| Data test | A check on the data. It fails when its query returns rows | [_staging.yml](models/staging/_staging.yml), [assert_no_negative_payments.sql](tests/assert_no_negative_payments.sql) |
| Lineage | A graph that shows which model depends on which | `dbt docs` (see below) |

## Project flow

```
seeds (CSV)  →  staging (views: clean and rename)  →  marts (final tables)
```

| Seed | Staging | Marts |
|---|---|---|
| `raw_customers` | `stg_customers` | `dim_customers` (table) |
| `raw_orders` | `stg_orders` | `dim_customers`, `fct_orders` (incremental) |
| `raw_payments` | `stg_payments` | `dim_customers`, `fct_orders` |

All objects are created in the `analytics` schema.

## Requirements

- Docker Desktop, running
- Python 3.14 (the version used to test this project)

## Setup

Run these commands in PowerShell, inside the `dbt` folder.

```powershell
py -3.14 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
docker compose up -d --wait
dbt debug
```

If PowerShell does not allow the activation script, use `.\.venv\Scripts\dbt.exe` instead of `dbt`.

`dbt debug` must end with `All checks passed!`. The connection is in [profiles.yml](profiles.yml). It only has local test credentials.

dbt may show a message about dbt v2. This project uses dbt Core v1, with the versions in [requirements.txt](requirements.txt).

## Main commands

| Command | What it does |
|---|---|
| `dbt seed` | Loads the CSV files |
| `dbt run` | Creates the models |
| `dbt test` | Runs the data tests |
| `dbt build` | Does all of the above, in dependency order. When a test fails, it skips the models that depend on that data |
| `dbt run --select +fct_orders` | Runs `fct_orders` and everything it needs |
| `dbt show --select dim_customers` | Shows some rows of a model |
| `dbt compile` | Turns Jinja into plain SQL |

Start with:

```powershell
dbt seed
dbt build
```

The last line must show `ERROR=0` and `TOTAL=35`: 3 seeds, 5 models and 27 tests.

**See the real SQL:** after a build, open `target/compiled/jaffle_intro/models/marts/fct_orders.sql`. You can see how dbt replaced `ref()` with real table names.

## Documentation and lineage

```powershell
dbt docs generate
dbt docs serve --port 8090
```

Open <http://127.0.0.1:8090>. The button at the bottom right opens the lineage graph. Press `Ctrl+C` to stop the server.

## The incremental model

`fct_orders` does not rebuild the whole table every time.

- **First run, or `--full-refresh`:** it loads all orders.
- **Next runs:** it only reads orders with an `updated_at` later than the newest one in the table. Then it replaces the rows with the same `order_id`.

This design has limits:

- If a payment is added or corrected, but the order `updated_at` does not change, `fct_orders.amount` keeps the old value.
- If an order changes, but its new `updated_at` is equal to or older than the newest value in the table, the change is ignored.
- If an order is removed from the source, its row stays in `fct_orders`.

In these cases, run `dbt build --full-refresh`.

`dim_customers` is a normal table, so dbt rebuilds it completely every time. Because of this, the two marts can show different totals until you do a full refresh.

## Exercise 1: make a test fail

1. In `seeds/raw_orders.csv`, change the status of order `1` from `returned` to `lost`.
2. Run `dbt build`.
3. The `accepted_values` test of `stg_orders.status` fails. dbt **skips** the models that depend on `stg_orders`, so the bad value does not reach the marts.
4. Change the value back to `returned` and run `dbt build` again. It must show `ERROR=0`.

The staging view still shows the bad row while the error exists. A view always reads the current data.

## Exercise 2: see the incremental model work

1. In `seeds/raw_orders.csv`, change order `99` to `completed` and set its `updated_at` to `2018-04-10 00:00:00`:

   ```
   99,85,2018-04-09,completed,2018-04-10 00:00:00
   ```

2. Add this line at the end of the same file:

   ```
   100,1,2018-04-10,placed,2018-04-10 00:00:00
   ```

3. Add this line at the end of `seeds/raw_payments.csv`:

   ```
   114,100,credit_card,2500
   ```

4. Run `dbt build`. In the `fct_orders` line, `INSERT 0 2` means that only two rows were written.
5. Check the result:

   ```powershell
   dbt show --inline "select * from {{ ref('fct_orders') }} where order_id >= 99 order by order_id"
   dbt show --inline "select count(*) as orders from {{ ref('fct_orders') }}"
   ```

   Order `99` is `completed`, order `100` has an amount of `25`, and there are 100 orders.

**Reset:** undo your three changes in the CSV files, then run `dbt build --full-refresh`. A normal build does not delete order `100` from `fct_orders`. After the full refresh there are 99 orders again.

## Troubleshooting

| Problem | What to do |
|---|---|
| `failed to connect to the docker API` | Start Docker Desktop and wait until it is ready |
| `port is already allocated` | Another program uses port 5435. Stop it, or change the port in `docker-compose.yml` and `profiles.yml` |
| `connection refused` in `dbt debug` | Run `docker compose up -d --wait` |
| A new test or model does not run | Run `dbt build --no-partial-parse` |

About the last row: dbt keeps a cache of the project files. While building this project, we wrote the tests before the models. dbt disabled those tests with a warning, and they stayed disabled after we created the models. The build ended with `ERROR=0` but ran no tests. Read the warnings, and check the `TOTAL` number.

## Stop the database

```powershell
docker compose down      # keeps the data
docker compose down -v   # deletes the data
```

## Data source

The CSV files come from [dbt-labs/jaffle-shop-classic](https://github.com/dbt-labs/jaffle-shop-classic), commit `fd7bfacae4f497ff044a6a0275268676bf1b64c3`, under the Apache License 2.0 (see [LICENSE-jaffle-shop-classic](LICENSE-jaffle-shop-classic)). That repository is archived.

**Change made here:** `raw_orders.csv` has a new column, `updated_at`, with the order date at midnight. The incremental model needs it. The models and tests in this folder were written for this project.

The source stores amounts without decimals. This project reads them as cents and divides them by 100.
