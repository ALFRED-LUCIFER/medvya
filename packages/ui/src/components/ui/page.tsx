import * as React from "react"
import { cn } from "#lib/utils"

/** The three page widths. One place sets them, so every page lines up with the shell (MDV audit UX-01). */
export const pageWidths = {
  /** Lists, boards, schedules, settings: use the workspace, up to the 1,600 px page maximum (MDV-P2-057). */
  fluid: "max-w-page",
  /** One form or one decision: a comfortable single column. */
  form: "max-w-form",
  /** Record and settings pages: wide enough for two columns, narrow enough to read. */
  reading: "max-w-reading",
} as const

export type PageWidth = keyof typeof pageWidths

/**
 * `fit` (MDV-P2-064, DESIGN.md "One screen per task"): from 1024 px the page is exactly one screen under the shell's top bar (--shell-top, less
 * the shell's 24 px padding above and below), so it never scrolls; a child with `min-h-0 overflow-y-auto` takes the rest and scrolls inside
 * itself. Under 1024 px and at 400 % zoom (short viewport) the page scrolls as usual.
 */
function PageContainer({
  width = "fluid",
  fit = false,
  className,
  ...props
}: React.ComponentProps<"div"> & { width?: PageWidth; fit?: boolean }) {
  return (
    <div
      data-slot="page-container"
      data-width={width}
      data-fit={fit || undefined}
      className={cn("flex w-full min-w-0 flex-col gap-4", pageWidths[width],
        fit && "wide:h-[calc(100dvh-var(--shell-top,0px)-3rem)] [@media(max-height:480px)]:h-auto", className)}
      {...props}
    />
  )
}

type PageHeaderProps = Omit<React.ComponentProps<"header">, "title"> & {
  title: React.ReactNode
  /** Small label above the title, e.g. the section name. */
  eyebrow?: React.ReactNode
  description?: React.ReactNode
  /** Buttons and status chips at the end of the title row; they wrap below it on narrow screens. */
  actions?: React.ReactNode
  /** A back link or button before the title (the caller names it and mirrors its arrow in right-to-left). */
  back?: React.ReactNode
  /** Secondary text after the title, such as the date. It may arrive after loading: nothing in the row moves when it does. */
  meta?: React.ReactNode
}

/** The page's single h1, in one 44 px row: back, title 20/28, meta, then the actions at the end (DESIGN.md Layout). */
function PageHeader({ title, eyebrow, description, actions, back, meta, className, children, ...props }: PageHeaderProps) {
  return (
    <header data-slot="page-header" className={cn("flex flex-col gap-1", className)} {...props}>
      {eyebrow && <p className="text-[13px] font-bold text-action-600">{eyebrow}</p>}
      <div data-slot="page-header-row" className="flex min-h-header flex-wrap items-center gap-x-3 gap-y-2">
        {/* A fixed basis: text arriving in the title or meta truncates instead of pushing the actions onto a new line. */}
        <div className="flex min-w-0 flex-[1_1_16rem] items-center gap-2">
          {back}
          <h1 className="min-w-0 truncate text-title">{title}</h1>
          {meta && <span data-slot="page-header-meta" className="shrink-0 text-body text-muted-foreground tabular-nums">{meta}</span>}
        </div>
        {actions && <div className="flex min-w-0 flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {description && <p className="text-body text-muted-foreground">{description}</p>}
      {children}
    </header>
  )
}

export { PageContainer, PageHeader }
