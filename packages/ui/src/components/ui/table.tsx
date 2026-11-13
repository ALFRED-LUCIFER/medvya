import * as React from "react"
import { ArrowDown, ArrowUp, ChevronsUpDown } from "lucide-react"
import { cn } from "#lib/utils"

type SortDirection = "ascending" | "descending" | "none"

// `label` is required: the scroll wrapper is a named, focusable region, so a keyboard user can scroll a wide table (WCAG 2.1.1).
function Table({ className, containerClassName, label, ...props }: React.ComponentProps<"table"> & { containerClassName?: string; label: string }) {
  return (
    <div
      data-slot="table-container"
      role="region"
      aria-label={label}
      tabIndex={0}
      className={cn("relative w-full overflow-x-auto rounded-lg border focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-1 focus-visible:outline-ring", containerClassName)}
    >
      <table
        data-slot="table"
        className={cn("w-full caption-bottom text-start text-sm", className)}
        {...props}
      />
    </div>
  )
}

function TableHeader({ className, sticky, ...props }: React.ComponentProps<"thead"> & { sticky?: boolean }) {
  return (
    <thead
      data-slot="table-header"
      className={cn("bg-muted [&_tr]:border-b", sticky && "sticky top-0 z-10", className)}
      {...props}
    />
  )
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return (
    <tbody
      data-slot="table-body"
      className={cn("[&_tr:last-child]:border-0", className)}
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
  return (
    <tr
      data-slot="table-row"
      className={cn(
        "border-b transition-colors hover:bg-muted/50 has-aria-expanded:bg-muted/50 data-[state=selected]:bg-muted data-[state=selected]:[&>:first-child]:border-s-4 data-[state=selected]:[&>:first-child]:border-primary",
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
  const Icon = sort === "ascending" ? ArrowUp : sort === "descending" ? ArrowDown : ChevronsUpDown
  return (
    <th
      data-slot="table-head"
      aria-sort={onSort ? sort ?? "none" : undefined}
      className={cn("h-11 px-3 align-middle font-bold whitespace-nowrap text-foreground", numeric ? "text-end" : "text-start", className)}
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

function TableCell({ className, numeric, ...props }: React.ComponentProps<"td"> & { numeric?: boolean }) {
  return (
    <td
      data-slot="table-cell"
      className={cn("min-h-11 p-3 align-middle", numeric && "text-end tabular-nums", className)}
      {...props}
    />
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

export {
  Table,
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
