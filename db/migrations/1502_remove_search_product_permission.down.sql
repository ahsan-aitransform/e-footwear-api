-- Rolls back 1502_remove_search_product_permission. An earlier version of the API adds the permission to the catalog again when it starts. Who held
-- it is not restored; the View Products permissions given instead are kept (they cannot be told apart from permissions an owner ticked by hand).
SELECT 1;
