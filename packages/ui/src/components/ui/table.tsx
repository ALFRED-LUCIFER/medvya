import * as React from "react"
import { ArrowDown, ArrowUp, ChevronsUpDown } from "lucide-react"
import { useI18n } from "@medvya/i18n"
import { Button } from "#components/ui/button"
import { cn } from "#lib/utils"

type SortDirection = "ascending" | "descending" | "none"

// Stacked: under 768 px each row is a block of labelled cells, so nothing scrolls sideways out of view (DESIGN.md, Tables).
// display:block drops table semantics in some browsers, so a stacked table states its roles explicitly. The header is for screen
// readers on a phone, and shows while a sort button in it has keyboard focus.
const TableLayout = React.createContext({ compact: false, stacked: false })

// Comfortable rows are at least 48 px, text-only ones too (DESIGN.md, Density); a stacked phone row is a block and keeps its own height.
const comfortableRows = (stacked: boolean) => (stacked ? "md:[&>tr]:h-row" : "[&>tr]:h-row")
// Compact rows are 36 px with a mouse and 48 px on any coarse pointer (DESIGN.md, Density). Stacked phone rows keep their own height.
// A row's button is 32 px with a mouse, so with the row's rule the row stays 36 px, and 44 px on a coarse pointer; its cell has no padding.
const compactRows =
  "[&>tr]:h-row-compact [&>tr>*]:h-auto [&>tr>*]:py-1.5 any-pointer-coarse:[&>tr]:h-row any-pointer-coarse:[&>tr>*]:py-3 " +
  "[&>tr>td:has(button)]:py-0 [&_button]:min-h-8 any-pointer-coarse:[&_button]:min-h-11"
const compactWideRows =
  "md:[&>tr]:h-row-compact md:[&>tr>*]:h-auto md:[&>tr>*]:py-1.5 md:any-pointer-coarse:[&>tr]:h-row md:any-pointer-coarse:[&>tr>*]:py-3 " +
  "md:[&>tr>td:has(button)]:py-0 md:[&_button]:min-h-8 md:any-pointer-coarse:[&_button]:min-h-11"

/**
 * `label` is required: the scroll wrapper is a named, focusable region, so a keyboard user can scroll a wide table (WCAG 2.1.1).
 * `density="compact"` is allowed on four tables only (audit log, formulary, order sets, staff; lint rule medvya-ui/compact-table).
 * `stacked` turns rows into labelled blocks under 768 px; give each TableCell its column `label`. `status` is announced politely (the sort).
 */
function Table({
  className,
  containerClassName,
  label,
  density,
  stacked = false,
  status,
  ...props
}: React.ComponentProps<"table"> & { containerClassName?: string; label: string; density?: "compact"; stacked?: boolean; status?: string }) {
  const layout = React.useMemo(() => ({ compact: density === "compact", stacked }), [density, stacked])
  return (
    <TableLayout.Provider value={layout}>
      <div
        data-slot="table-container"
        data-density={density ?? "comfortable"}
        role="region"
        aria-label={label}
        tabIndex={0}
        className={cn("relative w-full overflow-x-auto rounded-lg border focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-1 focus-visible:outline-ring", containerClassName)}
      >
        <table
          data-slot="table"
          role={stacked ? "table" : undefined}
          className={cn("w-full caption-bottom text-start text-sm", stacked && "max-md:block", className)}
          {...props}
        />
        {status !== undefined && <span role="status" className="sr-only">{status}</span>}
      </div>
    </TableLayout.Provider>
  )
}

function TableHeader({ className, sticky, ...props }: React.ComponentProps<"thead"> & { sticky?: boolean }) {
  const { stacked } = React.useContext(TableLayout)
  return (
    <thead
      data-slot="table-header"
      role={stacked ? "rowgroup" : undefined}
      className={cn("bg-muted [&_tr]:border-b", sticky && "sticky top-0 z-10", stacked && "max-md:sr-only max-md:focus-within:not-sr-only", className)}
      {...props}
    />
  )
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  const { compact, stacked } = React.useContext(TableLayout)
  return (
    <tbody
      data-slot="table-body"
      role={stacked ? "rowgroup" : undefined}
      className={cn("[&_tr:last-child]:border-0", compact ? (stacked ? compactWideRows : compactRows) : comfortableRows(stacked), stacked && "max-md:block", className)}
      {...props}
    />
  )
}

function TableFooter({ className, ...props }: React.ComponentProps<"tfoot">) {
  return (
    <tfoot
      data-slot="table-footer"
      className={cn("border-t bg-muted/50 font-medium [&>tr]:last:border-b-0", className)}
      {...props}
    />
  )
}

// Selected is also shown by a thick start-edge bar, never by the background alone.
function TableRow({ className, ...props }: React.ComponentProps<"tr">) {
  const { stacked } = React.useContext(TableLayout)
  return (
    <tr
      data-slot="table-row"
      role={stacked ? "row" : undefined}
      className={cn(
        "border-b transition-colors hover:bg-muted/50 has-aria-expanded:bg-muted/50 data-[state=selected]:bg-muted data-[state=selected]:[&>:first-child]:border-s-4 data-[state=selected]:[&>:first-child]:border-primary",
        stacked && "max-md:block max-md:py-2",
        className
      )}
      {...props}
    />
  )
}

/** Pass `onSort` (and the current `sort`) for a sortable column: it sets aria-sort and renders a 44 px button. */
function TableHead({
  className,
  numeric,
  sort,
  onSort,
  children,
  ...props
}: React.ComponentProps<"th"> & { numeric?: boolean; sort?: SortDirection; onSort?: () => void }) {
  const { stacked } = React.useContext(TableLayout)
  const Icon = sort === "ascending" ? ArrowUp : sort === "descending" ? ArrowDown : ChevronsUpDown
  return (
    <th
      data-slot="table-head"
      role={stacked ? (props.scope === "row" ? "rowheader" : "columnheader") : undefined}
      aria-sort={onSort ? sort ?? "none" : undefined}
      className={cn("h-11 px-3 align-middle font-bold whitespace-nowrap text-foreground", numeric ? "text-end" : "text-start", stacked && "max-md:block max-md:h-auto max-md:py-1 max-md:text-start max-md:whitespace-normal", className)}
      {...props}
    >
      {onSort ? (
        <button
          type="button"
          onClick={onSort}
          className={cn("inline-flex min-h-11 min-w-11 cursor-pointer items-center gap-1 font-bold focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-1 focus-visible:outline-ring", numeric && "flex-row-reverse")}
        >
          {children}
          <Icon aria-hidden className="size-4 shrink-0" />
        </button>
      ) : children}
    </th>
  )
}

/** In a stacked table, `label` (the column name) is shown above the value under 768 px; screen readers get the column header instead. */
function TableCell({ className, numeric, label, children, ...props }: React.ComponentProps<"td"> & { numeric?: boolean; label?: string }) {
  const { stacked } = React.useContext(TableLayout)
  return (
    <td
      data-slot="table-cell"
      role={stacked ? "cell" : undefined}
      className={cn("min-h-11 p-3 align-middle", numeric && "text-end tabular-nums", stacked && "max-md:block max-md:py-1 max-md:text-start", className)}
      {...props}
    >
      {stacked && label && <span aria-hidden className="block text-sm font-semibold text-muted-foreground md:hidden">{label}</span>}
      {children}
    </td>
  )
}

function TableCaption({ className, ...props }: React.ComponentProps<"caption">) {
  return (
    <caption
      data-slot="table-caption"
      className={cn("mt-4 text-sm text-muted-foreground", className)}
      {...props}
    />
  )
}

/** The one empty, loading and error pattern for every table: shows the message, or the table when there are rows. */
function TableState({
  state,
  messages,
  children,
}: {
  state: "loading" | "error" | "empty" | "ready"
  messages: { loading: string; error: string; empty: string }
  children: React.ReactNode
}) {
  if (state === "ready") return children
  if (state === "error") return <p role="alert" data-slot="table-state" className="text-sm font-semibold text-danger-700">{messages.error}</p>
  return <p role="status" data-slot="table-state" className={state === "empty" ? "font-bold" : undefined}>{messages[state]}</p>
}

/** Previous and next for a table over one page (50 rows); nothing when everything fits on one page. `page` counts from 0. */
function TablePagination({ page, pageCount, onPage, className }: { page: number; pageCount: number; onPage: (page: number) => void; className?: string }) {
  const { t } = useI18n()
  if (pageCount <= 1) return null
  return (
    <nav data-slot="table-pagination" aria-label={t("table.pages")} className={cn("flex flex-wrap items-center gap-3", className)}>
      <Button variant="outline" disabled={page === 0} onClick={() => onPage(page - 1)}>{t("table.previous")}</Button>
      <span aria-live="polite" className="text-sm font-semibold tabular-nums">{t("table.page", { page: page + 1, count: pageCount })}</span>
      <Button variant="outline" disabled={page >= pageCount - 1} onClick={() => onPage(page + 1)}>{t("table.next")}</Button>
    </nav>
  )
}

export {
  Table,
  TablePagination,
  TableHeader,
  TableBody,
  TableFooter,
  TableHead,
  TableRow,
  TableCell,
  TableCaption,
  TableState,
}
export type { SortDirection }
