using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Iedora.Menus.Migrations
{
    /// <inheritdoc />
    public partial class MenuAnalyticsEfficient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF's scaffold emits bare ALTER COLUMN … TYPE, which Postgres refuses for text→uuid /
            // text→date (no implicit cast). We rewrite those as explicit USING casts so the real
            // counter data survives the type tightening; only view_seen (ephemeral dedup markers)
            // is rebuilt from scratch.

            // view_seen: rebuild fresh with the tight (uuid, uuid, timestamptz) key. Its rows are
            // per-hour dedup markers — dropping in-flight ones at most re-counts a visitor once
            // this hour, so a rebuild is cheaper than juggling the PK across a column swap.
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS menu.view_seen;");
            migrationBuilder.Sql(@"
                CREATE TABLE menu.view_seen (
                    ""VisitorId""    uuid        NOT NULL,
                    ""RestaurantId"" uuid        NOT NULL REFERENCES menu.restaurants(""Id"") ON DELETE CASCADE,
                    ""HourStart""    timestamptz NOT NULL,
                    CONSTRAINT ""PK_view_seen"" PRIMARY KEY (""VisitorId"", ""RestaurantId"", ""HourStart"")
                );");

            // item_view_seen: drop the redundant audit column + its index, tighten key types in place.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS menu.""IX_item_view_seen_SeenAt"";");
            migrationBuilder.Sql(@"ALTER TABLE menu.item_view_seen DROP COLUMN IF EXISTS ""SeenAt"";");
            migrationBuilder.Sql(@"ALTER TABLE menu.item_view_seen
                ALTER COLUMN ""VisitorId"" TYPE uuid USING ""VisitorId""::uuid,
                ALTER COLUMN ""Day""       TYPE date USING ""Day""::date;");

            // Counters + sessions: Day text→date, session duration int→smallint (clamped ≤ 3600).
            migrationBuilder.Sql(@"ALTER TABLE menu.daily_view ALTER COLUMN ""Day"" TYPE date USING ""Day""::date;");
            migrationBuilder.Sql(@"ALTER TABLE menu.item_view  ALTER COLUMN ""Day"" TYPE date USING ""Day""::date;");
            migrationBuilder.Sql(@"ALTER TABLE menu.menu_session
                ALTER COLUMN ""Day""             TYPE date     USING ""Day""::date,
                ALTER COLUMN ""DurationSeconds"" TYPE smallint USING ""DurationSeconds""::smallint;");

            // Bound bloat on the UPDATE-heavy counter tables the public beacon hammers: leave HOT-update
            // headroom (fillfactor) and let autovacuum sweep them in small frequent passes. (Bun's 0005.)
            migrationBuilder.Sql(@"ALTER TABLE menu.daily_view SET (fillfactor = 80, autovacuum_vacuum_scale_factor = 0.02);");
            migrationBuilder.Sql(@"ALTER TABLE menu.item_view  SET (fillfactor = 80, autovacuum_vacuum_scale_factor = 0.02);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE menu.daily_view RESET (fillfactor, autovacuum_vacuum_scale_factor);");
            migrationBuilder.Sql(@"ALTER TABLE menu.item_view  RESET (fillfactor, autovacuum_vacuum_scale_factor);");

            migrationBuilder.Sql(@"ALTER TABLE menu.menu_session
                ALTER COLUMN ""Day""             TYPE text    USING to_char(""Day"", 'YYYY-MM-DD'),
                ALTER COLUMN ""DurationSeconds"" TYPE integer USING ""DurationSeconds""::integer;");
            migrationBuilder.Sql(@"ALTER TABLE menu.item_view  ALTER COLUMN ""Day"" TYPE text USING to_char(""Day"", 'YYYY-MM-DD');");
            migrationBuilder.Sql(@"ALTER TABLE menu.daily_view ALTER COLUMN ""Day"" TYPE text USING to_char(""Day"", 'YYYY-MM-DD');");

            migrationBuilder.Sql(@"ALTER TABLE menu.item_view_seen
                ALTER COLUMN ""VisitorId"" TYPE text USING ""VisitorId""::text,
                ALTER COLUMN ""Day""       TYPE text USING to_char(""Day"", 'YYYY-MM-DD');");
            migrationBuilder.Sql(@"ALTER TABLE menu.item_view_seen ADD COLUMN ""SeenAt"" timestamptz NOT NULL DEFAULT now();");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_item_view_seen_SeenAt"" ON menu.item_view_seen (""SeenAt"");");

            migrationBuilder.Sql(@"DROP TABLE IF EXISTS menu.view_seen;");
            migrationBuilder.Sql(@"
                CREATE TABLE menu.view_seen (
                    ""VisitorId""    text        NOT NULL,
                    ""RestaurantId"" uuid        NOT NULL REFERENCES menu.restaurants(""Id"") ON DELETE CASCADE,
                    ""HourBucket""   text        NOT NULL,
                    ""SeenAt""       timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT ""PK_view_seen"" PRIMARY KEY (""VisitorId"", ""RestaurantId"", ""HourBucket"")
                );");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_view_seen_SeenAt"" ON menu.view_seen (""SeenAt"");");
        }
    }
}
