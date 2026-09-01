namespace Eiri.Reimbursement.Infrastructure.Sqlite;

internal static class Schema
{
    internal const int CurrentVersion = 2;

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
}
