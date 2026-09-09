namespace Eiri.Reimbursement.Infrastructure.Sqlite;

internal static class Schema
{
    internal const int CurrentVersion = 7;

    internal const string Version7 =
        """
        BEGIN;
        CREATE TABLE IF NOT EXISTS dingtalk_submission_info (
            id INTEGER PRIMARY KEY CHECK (id = 1) REFERENCES dingtalk_connection(id) ON DELETE CASCADE,
            dept_id INTEGER NOT NULL CHECK (dept_id > 0),
            department_name TEXT NOT NULL,
            user_id TEXT NULL,
            user_name TEXT NULL,
            CHECK ((user_id IS NULL) = (user_name IS NULL))
        );
        PRAGMA user_version = 7;
        COMMIT;
        """;

    internal const string Version6 =
        """
        BEGIN;
        CREATE TABLE IF NOT EXISTS dingtalk_connection (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            client_id TEXT NOT NULL,
            client_secret TEXT NOT NULL,
            access_token TEXT NOT NULL
        );
        PRAGMA user_version = 6;
        COMMIT;
        """;

    internal const string Version1 =
        """
        CREATE TABLE IF NOT EXISTS orders (
            id TEXT PRIMARY KEY,
            platform TEXT NOT NULL,
            external_order_number TEXT NULL,
            notes TEXT NULL,
            exported_at TEXT NULL,
            submitted_at TEXT NULL,
            refunded_at TEXT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS managed_files (
            id TEXT PRIMARY KEY,
            order_id TEXT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            role TEXT NOT NULL CHECK (role IN ('OrderScreenshot', 'InvoicePdf')),
            relative_path TEXT NOT NULL UNIQUE,
            media_type TEXT NOT NULL,
            byte_length INTEGER NOT NULL CHECK (byte_length >= 0),
            sha256 TEXT NOT NULL UNIQUE,
            processing_state TEXT NOT NULL,
            processing_error TEXT NULL,
            imported_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS invoices (
            id TEXT PRIMARY KEY,
            order_id TEXT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            managed_file_id TEXT NOT NULL UNIQUE REFERENCES managed_files(id) ON DELETE RESTRICT,
            merchant_name TEXT NOT NULL DEFAULT '',
            invoice_number TEXT NOT NULL DEFAULT '',
            total_minor_units INTEGER NOT NULL DEFAULT 0,
            currency TEXT NOT NULL DEFAULT 'CNY' CHECK (currency = 'CNY'),
            needs_review INTEGER NOT NULL DEFAULT 1 CHECK (needs_review IN (0, 1)),
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS invoice_lines (
            id TEXT PRIMARY KEY,
            invoice_id TEXT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
            sequence INTEGER NOT NULL CHECK (sequence >= 0),
            name TEXT NOT NULL,
            amount_minor_units INTEGER NULL,
            is_effective INTEGER NOT NULL DEFAULT 1 CHECK (is_effective IN (0, 1)),
            UNIQUE (invoice_id, sequence)
        );

        CREATE TABLE IF NOT EXISTS extraction_results (
            managed_file_id TEXT PRIMARY KEY REFERENCES managed_files(id) ON DELETE CASCADE,
            worker_version TEXT NOT NULL,
            parser_version TEXT NOT NULL,
            candidates_json TEXT NOT NULL,
            completed_at TEXT NULL,
            error TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_orders_created_at ON orders(created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_orders_platform ON orders(platform);
        CREATE INDEX IF NOT EXISTS idx_managed_files_order_id ON managed_files(order_id);
        CREATE INDEX IF NOT EXISTS idx_invoices_order_id ON invoices(order_id);
        CREATE INDEX IF NOT EXISTS idx_invoices_invoice_number ON invoices(invoice_number);

        PRAGMA user_version = 1;
        """;

    internal const string Version2 =
        """
        ALTER TABLE managed_files ADD COLUMN original_file_name TEXT NOT NULL DEFAULT '';
        PRAGMA user_version = 2;
        """;

    internal const string Version3 =
        """
        ALTER TABLE invoices ADD COLUMN is_user_corrected INTEGER NOT NULL DEFAULT 0
            CHECK (is_user_corrected IN (0, 1));
        UPDATE invoices
        SET is_user_corrected = 1
        WHERE needs_review = 0
           OR merchant_name <> ''
           OR invoice_number <> ''
           OR total_minor_units <> 0;
        UPDATE extraction_results
        SET candidates_json = json_extract(candidates_json, '$.candidates')
        WHERE json_valid(candidates_json)
          AND json_type(candidates_json) = 'object'
          AND json_type(candidates_json, '$.candidates') = 'array';
        PRAGMA user_version = 3;
        """;
    internal const string Version4 =
        """
        BEGIN IMMEDIATE;
        CREATE TABLE reimbursement_forms (
            id TEXT PRIMARY KEY,
            application_date TEXT NULL,
            reimbursement_type TEXT NOT NULL DEFAULT '',
            content TEXT NOT NULL DEFAULT '',
            total_minor_units INTEGER NULL,
            has_analysis INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );
        ALTER TABLE orders ADD COLUMN reimbursement_id TEXT NULL REFERENCES reimbursement_forms(id) ON DELETE RESTRICT;
        CREATE INDEX idx_orders_reimbursement_id ON orders(reimbursement_id);
        CREATE TABLE reimbursement_files (
            id TEXT PRIMARY KEY,
            reimbursement_id TEXT NOT NULL REFERENCES reimbursement_forms(id) ON DELETE RESTRICT,
            original_file_name TEXT NOT NULL,
            relative_path TEXT NOT NULL UNIQUE,
            byte_length INTEGER NOT NULL,
            sha256 TEXT NOT NULL,
            processing_error TEXT NULL,
            imported_at TEXT NOT NULL,
            UNIQUE(reimbursement_id, sha256)
        );
        PRAGMA user_version = 4;
        COMMIT;
        """;
    internal const string Version5 =
        """
        BEGIN IMMEDIATE;
        ALTER TABLE reimbursement_forms ADD COLUMN exported_at TEXT NULL;
        ALTER TABLE reimbursement_forms ADD COLUMN submitted_at TEXT NULL;
        ALTER TABLE reimbursement_forms ADD COLUMN refunded_at TEXT NULL;
        PRAGMA user_version = 5;
        COMMIT;
        """;
}
