import * as React from "react"
import { cn } from "#lib/utils"
import { Sheet, SheetContent, SheetTitle } from "#components/ui/sheet"

// The `wide:` breakpoint (1024 px): two panes at and above it, a drawer below.
const wideQuery = "(min-width: 1024px)"
const subscribe = (onChange: () => void) => {
  const m = window.matchMedia(wideQuery)
  m.addEventListener("change", onChange)
  return () => m.removeEventListener("change", onChange)
}
/** True at the `wide:` tier (1024 px and wider), live as the window resizes: for a layout whose semantics change there, not only its CSS. */
const useWide = () => React.useSyncExternalStore(subscribe, () => window.matchMedia(wideQuery).matches)

const sideWidths = {
  panel: { aside: "w-panel", sheet: "max-w-panel" },
  /** The consultation chart panel. */
  chart: { aside: "w-panel-chart", sheet: "max-w-panel-chart" },
} as const

type SplitLayoutProps = React.ComponentProps<"div"> & {
  /** The side pane's content. Rendered once: beside the main pane at 1024 px and wider, otherwise in a Sheet. */
  side: React.ReactNode
  /** Names the side pane (the aside's label, the sheet's title). */
  sideLabel: string
  sideWidth?: keyof typeof sideWidths
  /** Under 1024 px: whether the sheet is open. The caller's opener (hidden from `wide:`) sets it; focus returns there on close. */
  open: boolean
  onOpenChange: (open: boolean) => void
  closeLabel: string
}

function SplitLayout({ side, sideLabel, sideWidth = "panel", open, onOpenChange, closeLabel, className, children, ...props }: SplitLayoutProps) {
  const wide = useWide()
  return (
    <div data-slot="split-layout" className={cn("flex min-w-0 gap-4", className)} {...props}>
      <div className="flex min-w-0 flex-1 flex-col gap-4">{children}</div>
      {wide ? (
        <aside aria-label={sideLabel} className={cn("flex shrink-0 flex-col gap-3", sideWidths[sideWidth].aside)}>{side}</aside>
      ) : (
        <Sheet open={open} onOpenChange={onOpenChange}>
          <SheetContent closeLabel={closeLabel} aria-describedby={undefined} className={sideWidths[sideWidth].sheet}>
            <SheetTitle>{sideLabel}</SheetTitle>
            {side}
          </SheetContent>
        </Sheet>
      )}
    </div>
  )
}

export { SplitLayout, useWide }
