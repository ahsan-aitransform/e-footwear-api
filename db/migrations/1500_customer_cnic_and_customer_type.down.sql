-- Rolls back 1500_customer_cnic_and_customer_type. The CNIC numbers and customer types that were entered are lost.
ALTER TABLE contacts.customers
    DROP COLUMN customer_type,
    DROP COLUMN cnic;
