{{
    config(
        materialized='incremental',
        unique_key='order_id',
        incremental_strategy='delete+insert'
    )
}}

with orders as (
    select * from {{ ref('stg_orders') }}

    -- is_incremental() is false on the first run and with --full-refresh,
    -- so those runs load every order.
    {% if is_incremental() %}
    -- Later runs read only orders changed after the newest one already loaded.
    -- "this" means the table that this model built before.
    where updated_at > (select max(updated_at) from {{ this }})
    {% endif %}
),

order_payments as (
    select order_id, sum(amount) as amount
    from {{ ref('stg_payments') }}
    group by order_id
)

-- delete+insert with unique_key: dbt deletes the rows that have the same
-- order_id and then inserts the new version.
select
    orders.order_id,
    orders.customer_id,
    orders.order_date,
    orders.status,
    orders.updated_at,
    coalesce(order_payments.amount, 0) as amount
from orders
left join order_payments on order_payments.order_id = orders.order_id
