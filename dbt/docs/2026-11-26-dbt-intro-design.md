# Diseño: introducción práctica a dbt

## Objetivo
Entender qué es dbt y cómo funciona, sin buscar nivel experto.

## Alcance
- Proyecto autocontenido en `dbt/`. No usa código, datos ni contenedores de otras carpetas.
- Fuera de alcance: carga desde sistemas externos, orquestación, CI, dbt Cloud, informes y macros propias.
- Sería un error que preparar el entorno pesara más que aprender dbt.

## Enfoques
| Enfoque | Costo y beneficio |
|---|---|
| **A. Datos de ejemplo en CSV (elegido)** | Simple y autocontenido. `dbt seed` no representa una carga real. |
| B. Datos de los servicios de otra carpeta | Más completo, pero crea una dependencia y exige escribir una carga. |

Se eligió A porque la prueba debe ser independiente. DuckDB se descartó porque el repo ya usa PostgreSQL.

## Entorno
- PostgreSQL 16 con un `docker-compose.yml` propio, en `127.0.0.1:5435`.
- Python 3.14 con un entorno virtual en `dbt/.venv`.
- `dbt-core` y `dbt-postgres`, con versiones fijadas en `requirements.txt`.
- `profiles.yml` dentro de `dbt/`, solo con credenciales locales de prueba.
- `.gitignore` propio para `.venv/`, `target/`, `logs/` y `dbt_packages/`.

## Datos y modelos
Los datos son los tres CSV de [jaffle-shop-classic](https://github.com/dbt-labs/jaffle-shop-classic) (dbt Labs, Apache 2.0), una tienda ficticia. Se copian en el proyecto junto con su licencia, y se indican el origen y los cambios. Solo se usan los datos: los modelos y tests se escriben desde cero.

| Capa | Objetos | Qué se aprende |
|---|---|---|
| Seeds | `raw_customers`, `raw_orders`, `raw_payments` | `dbt seed` |
| Staging (vistas) | `stg_customers`, `stg_orders`, `stg_payments` | limpiar, tipar, `ref()` |
| Marts (tabla) | `dim_customers` | dependencias y materializaciones |
| Marts (incremental) | `fct_orders` | procesar solo pedidos nuevos o modificados |

A la copia de `raw_orders` se le agrega una fecha de actualización, que el original no tiene y que usa el modelo incremental.

## Tests y documentación
- Tests `unique`, `not_null`, `relationships` y `accepted_values`.
- Un test propio en SQL: ningún pago con importe negativo.
- Descripciones en YAML, y `dbt docs generate` y `dbt docs serve` para ver el linaje.

## Guía de aprendizaje
`README.md` (inglés B1/B2) y `README.es.md`, con:
1. Qué es dbt y qué no hace.
2. Cómo levantar el entorno y ejecutar cada comando.
3. Ejercicio: introducir un dato erróneo y ver fallar un test.
4. Ejercicio: agregar un pedido y ver cómo actúa el modelo incremental.

## Resultado esperado
Con la base vacía, `dbt seed` y `dbt build` terminan sin errores, los dos ejercicios se comportan como describe la guía y se puede ver el linaje en el navegador.

## Decisiones de implementación
Quedan a cargo de la implementación los nombres de columnas, el contenido de los CSV y la estructura interna de carpetas.
