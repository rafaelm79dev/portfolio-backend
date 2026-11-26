# dbt Intro Implementation Plan

**Goal:** Un proyecto dbt local y autocontenido para aprender cómo funciona dbt.

**Architecture:** Tres CSV se cargan con `dbt seed` en un PostgreSQL propio. Las vistas de staging limpian y tipan, y dos marts (una tabla y un modelo incremental) preparan el resultado. Todo queda en el esquema `analytics`.

**Tech Stack:** Python 3.14.7, dbt-core 1.12.7, dbt-postgres 1.11.0, PostgreSQL 16 en Docker.

**Spec:** `dbt/docs/2026-11-26-dbt-intro-design.md`

## Global Constraints

- Solo se crean o modifican archivos dentro de `dbt/`.
- No se hacen commits ni otros cambios de historial: los ejecuta el usuario. Cada tarea termina sugiriendo el comando.
- Sin menciones a herramientas de IA en archivos ni en mensajes de commit.
- PostgreSQL: imagen `postgres:16-alpine`, publicado en `127.0.0.1:5435`. Base, usuario y contraseña: `jaffle`.
- Datos: `dbt-labs/jaffle-shop-classic`, commit `fd7bfacae4f497ff044a6a0275268676bf1b64c3`, Apache 2.0.
- Proyecto y perfil dbt: `jaffle_intro`. Esquema: `analytics`.
- Los comandos se ejecutan en PowerShell desde `dbt/`, usando `.\.venv\Scripts\dbt.exe` (abajo, `dbt`).
- Sin paquetes dbt ni macros propias.

## Review Focus

- Un pago nuevo o corregido sin cambiar `updated_at` del pedido: `fct_orders.amount` queda viejo. La guía lo explica y muestra `dbt build --full-refresh`.
- Un pedido modificado con `updated_at` igual o anterior al máximo cargado: el incremental no lo toma. La guía lo explica en el ejercicio 2.
- Volver a ejecutar `dbt build` sin cambios: `fct_orders` sigue con las mismas filas y sin duplicados (Task 3).
- Puerto 5435 ocupado o Docker apagado: la guía dice cómo reconocerlo.
- Deshacer los ejercicios: la guía indica cómo restaurar los CSV y ejecutar `dbt build --full-refresh`, ya que un incremental no borra filas quitadas del origen.

## File Structure

```
dbt/
  docker-compose.yml  requirements.txt  .gitignore
  dbt_project.yml     profiles.yml      LICENSE-jaffle-shop-classic
  seeds/              raw_customers.csv, raw_orders.csv, raw_payments.csv
  models/staging/     stg_customers.sql, stg_orders.sql, stg_payments.sql, _staging.yml
  models/marts/       dim_customers.sql, fct_orders.sql, _marts.yml
  tests/              assert_no_negative_payments.sql
  README.md           README.es.md
```

---

### Task 1: Entorno y seeds

**Files:** Create `docker-compose.yml`, `requirements.txt`, `.gitignore`, `dbt_project.yml`, `profiles.yml`, `LICENSE-jaffle-shop-classic`, `seeds/*.csv`

**Interfaces:** Produces las tablas `raw_customers(id, first_name, last_name)`, `raw_orders(id, user_id, order_date date, status, updated_at timestamp)` y `raw_payments(id, order_id, payment_method, amount)`, con importes en centavos.

- [ ] **Step 1: Crear la configuración.**
  - `docker-compose.yml`: nombre de proyecto `dbt-intro`, un servicio `warehouse` con healthcheck `pg_isready -U jaffle -d jaffle` y volumen `warehouse-data`.
  - `requirements.txt`: `dbt-core==1.12.7` y `dbt-postgres==1.11.0`.
  - `.gitignore`: `.venv/`, `target/`, `logs/`, `dbt_packages/`, `.user.yml`.
  - `profiles.yml`: target `dev`, tipo `postgres`, `localhost:5435`, `threads: 4`.
  - `dbt_project.yml`: `staging` como `view`, `marts` como `table`. En seeds, `raw_orders` fija `order_date: date` y `updated_at: timestamp`.
- [ ] **Step 2: Descargar** los tres CSV y `LICENSE` del commit fijado. Agregar a `raw_orders.csv` la columna `updated_at` con el valor `<order_date> 00:00:00`. No cambiar nada más.
- [ ] **Step 3: Instalar.** `py -3.14 -m venv .venv` y `.\.venv\Scripts\python.exe -m pip install -r requirements.txt`. `dbt --version` muestra core 1.12.7 y postgres 1.11.0. Si no instala o no arranca en 3.14, detenerse e informar.
- [ ] **Step 4: Levantar la base** (requiere Docker Desktop abierto): `docker compose up -d --wait`. Luego `dbt debug` termina con `All checks passed!`.
- [ ] **Step 5: Cargar.** `dbt seed` termina con 3 seeds sin errores, de 100, 99 y 113 filas.
- [ ] **Step 6: Revisar git.** `git status --short --untracked-files=all .` lista los archivos creados, incluidos los CSV, y nada de `.venv/`, `target/` ni `logs/`.
- [ ] **Step 7: Sugerir commit:** `feat(dbt): add local environment and sample data`

### Task 2: Staging

**Files:** Create `models/staging/_staging.yml`, `stg_customers.sql`, `stg_orders.sql`, `stg_payments.sql`, `tests/assert_no_negative_payments.sql`

**Interfaces:** Consumes los seeds de Task 1 mediante `ref()`. Produces:
- `stg_customers(customer_id, first_name, last_name)`
- `stg_orders(order_id, customer_id, order_date, status, updated_at)`
- `stg_payments(payment_id, order_id, payment_method, amount numeric(10,2))`, con `amount` = centavos / 100

- [ ] **Step 1: Escribir los tests** y las descripciones de modelos y columnas.
  - `unique` y `not_null` en las tres claves primarias.
  - `relationships`: `stg_orders.customer_id` → `stg_customers`, y `stg_payments.order_id` → `stg_orders`.
  - `accepted_values` en `status`: `placed, shipped, completed, return_pending, returned`.
  - `accepted_values` en `payment_method`: `credit_card, coupon, bank_transfer, gift_card`.
  - `assert_no_negative_payments.sql` devuelve las filas de `stg_payments` con `amount < 0`.
- [ ] **Step 2: Verificar que falla.** `dbt build` falla porque `stg_payments` no existe.
- [ ] **Step 3: Crear las tres vistas** según las interfaces, con CTE y sin filtrar filas.
- [ ] **Step 4: Verificar.** `dbt build` termina con `ERROR=0`. `dbt show --inline "select sum(amount) as total from {{ ref('stg_payments') }}"` devuelve `1672.00`.
- [ ] **Step 5: Sugerir commit:** `feat(dbt): add staging models and data tests`

### Task 3: Marts

**Files:** Create `models/marts/_marts.yml`, `dim_customers.sql`, `fct_orders.sql`

**Interfaces:** Consumes los modelos de Task 2. Produces:
- `dim_customers(customer_id, first_name, last_name, first_order_date, last_order_date, number_of_orders, total_paid)`. Incluye clientes sin pedidos, con `0` en los dos últimos y fechas nulas. `total_paid` suma todos los pagos, incluidos los de pedidos devueltos.
- `fct_orders(order_id, customer_id, order_date, status, updated_at, amount)`. Una fila por pedido, `amount` `0` si no tiene pagos.

- [ ] **Step 1: Escribir los tests** y las descripciones: `unique` y `not_null` en `customer_id` y `order_id`; `not_null` en `number_of_orders`, `total_paid` y `amount`; `relationships` de `fct_orders.customer_id` → `dim_customers`.
- [ ] **Step 2: Verificar que faltan los modelos.** `dbt parse --no-partial-parse` avisa que no encuentra los nodos `dim_customers` y `fct_orders` descritos en `_marts.yml`.
- [ ] **Step 3: Crear los modelos.** Agregar pagos por pedido antes de unir, para no duplicar filas. `fct_orders`: `materialized='incremental'`, `unique_key='order_id'`, `incremental_strategy='delete+insert'`; con `is_incremental()`, solo filas con `updated_at > (select max(updated_at) from {{ this }})`. Comentar en el SQL qué hace cada parte del bloque incremental.
- [ ] **Step 4: Verificar.** `dbt build --full-refresh` termina con `ERROR=0`. Con `dbt show --inline`: `dim_customers` tiene 100 filas (38 con `number_of_orders = 0`), `fct_orders` tiene 99, y ambas sumas de importes dan `1672.00`.
- [ ] **Step 5: Repetir** `dbt build`. `ERROR=0` y `fct_orders` sigue con 99 filas.
- [ ] **Step 6: Sugerir commit:** `feat(dbt): add customer dimension and incremental orders fact`

### Task 4: Guía y documentación

**Files:** Create `README.md` (inglés B1/B2), `README.es.md` (mismo contenido). Modify `docs/2026-11-26-dbt-intro-design.md` solo si la implementación se apartó del diseño.

**Interfaces:** Consumes los comandos y modelos de las tareas 1 a 3.

- [ ] **Step 1: Escribir las guías**, enlazadas entre sí, con:
  - Qué es dbt y qué no hace, y los conceptos seed, model, `ref()`, materialization, test y lineage, cada uno señalando un archivo del proyecto.
  - Diagrama simple: `seeds → staging → marts`.
  - Puesta en marcha y cada comando con lo que hace, incluido mirar el SQL compilado en `target/compiled/`.
  - Origen de los datos (URL, commit, licencia) y el único cambio: la columna `updated_at`.
  - Ejercicio 1: en `raw_orders.csv`, cambiar un `status` a `lost`; `dbt build` falla en `accepted_values` y omite los modelos que dependen de `stg_orders`.
  - Ejercicio 2: agregar el pedido `100` y el pago `114`, y cambiar un pedido `placed` a `completed`, ambos pedidos con `updated_at` de `2018-04-10 00:00:00`; tras `dbt build`, `fct_orders` tiene 100 filas y el estado actualizado.
  - Los puntos de Review Focus y cómo apagar la base (`docker compose down`, y `-v` para borrar los datos).
- [ ] **Step 2: Probar los ejercicios** siguiendo la guía al pie de la letra. Confirmar el resultado descrito, restaurar los CSV y ejecutar `dbt build --full-refresh`: `ERROR=0`, 99 pedidos.
- [ ] **Step 3: Probar desde cero.** `docker compose down -v`, `docker compose up -d --wait`, `dbt seed`, `dbt build`: `ERROR=0`.
- [ ] **Step 4: Verificar el linaje.** `dbt docs generate` termina sin errores. Ejecutar `dbt docs serve --port 8090` en segundo plano, comprobar que `http://localhost:8090` responde 200 y detenerlo. El diagrama lo revisa el usuario en el navegador.
- [ ] **Step 5: Revisar git.** `git status --short` desde la raíz no muestra cambios fuera de `dbt/`, aparte de `.vscode/` que ya estaba.
- [ ] **Step 6: Sugerir commit:** `docs(dbt): add bilingual learning guide`
