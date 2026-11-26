with customers as (
    select * from {{ ref('stg_customers') }}
),

orders as (
    select * from {{ ref('stg_orders') }}
),

payments as (
    select * from {{ ref('stg_payments') }}
),

-- one row per order, so the join below does not repeat orders
order_payments as (
    select order_id, sum(amount) as amount
    from payments
    group by order_id
),

customer_orders as (
    select
        orders.customer_id,
        min(orders.order_date) as first_order_date,
        max(orders.order_date) as last_order_date,
        count(*) as number_of_orders,
        sum(order_payments.amount) as total_paid
    from orders
    left join order_payments on order_payments.order_id = orders.order_id
    group by orders.customer_id
)

select
    customers.customer_id,
    customers.first_name,
    customers.last_name,
    customer_orders.first_order_date,
    customer_orders.last_order_date,
    coalesce(customer_orders.number_of_orders, 0) as number_of_orders,
    coalesce(customer_orders.total_paid, 0) as total_paid
from customers
left join customer_orders on customer_orders.customer_id = customers.customer_id
