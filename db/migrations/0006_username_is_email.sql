-- 0006: sign-in looks users up by the username column, and the email is now the sign-in name.
-- Users created before 0005 still carry their old usernames, so set username = email for every user.
BEGIN;
UPDATE sec.users SET username = email WHERE username <> email;
COMMIT;
