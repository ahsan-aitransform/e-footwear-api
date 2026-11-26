-- 1500_customer_cnic_and_customer_type
-- New Customer gets two fields: the CNIC (national identity card number, optional) and the customer type (regular or special).
-- Every existing customer, including the walk-in customer "Cash Sale", becomes REGULAR.
ALTER TABLE contacts.customers
    ADD COLUMN cnic text,
    ADD COLUMN customer_type text NOT NULL DEFAULT 'REGULAR' CHECK (customer_type IN ('REGULAR', 'SPECIAL'));
