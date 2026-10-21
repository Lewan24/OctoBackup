#!/bin/sh
# Docker's MySQL entrypoint runs this only for a new development database volume.
case "$MYSQL_USER" in
    ''|*[!a-zA-Z0-9_]*) echo 'MYSQL_USER must contain only letters, digits or underscores.' >&2; exit 1 ;;
esac
# Database-scoped grants are already created by the official entrypoint. Routines
# export additionally needs this metadata privilege; table data stays database-scoped.
MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql --user=root \
    --execute="GRANT SHOW_ROUTINE ON *.* TO '$MYSQL_USER'@'%';"
