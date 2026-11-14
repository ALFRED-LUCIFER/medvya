import * as React from "react"
import { cn, scrollContainer } from "#lib/utils"

/**
 * The commit footer (MDV-P1-082): put it last in the page flow. It is part of the layout, never an overlay: it reserves its own space
 * (56 px, more when its actions wrap), pins to the bottom while the page scrolls, and sets its scroll container's scroll-padding-bottom
 * to its height so a focused or scrolled-to field is never under it (WCAG 2.2 SC 2.4.11). At 400 % zoom (short viewport) it un-pins.
 * `status` sits at the start (usually SaveStatus); the children are the commit actions, at the end.
 */
function StickyActionBar({ status, className, children, ...props }: React.ComponentProps<"footer"> & { status?: React.ReactNode }) {
  const ref = React.useRef<HTMLElement>(null)
  React.useEffect(() => {
    const el = ref.current
    if (!el || typeof ResizeObserver === "undefined") return
    const container = scrollContainer(el)
    const observer = new ResizeObserver(() => { container.style.scrollPaddingBottom = `${el.offsetHeight}px` })
    observer.observe(el)
    // Chromium skips the focus scroll when the element is inside the viewport, even if it is under the bar; scroll it clear (honours the padding).
    const onFocus = (e: FocusEvent) => {
      const target = e.target as HTMLElement
      if (el.contains(target) || !(target instanceof HTMLElement)) return
      const t = target.getBoundingClientRect(), b = el.getBoundingClientRect()
      if (t.bottom > b.top && t.top < b.bottom) target.scrollIntoView({ block: "nearest" })
    }
    document.addEventListener("focusin", onFocus)
    return () => { observer.disconnect(); document.removeEventListener("focusin", onFocus); container.style.scrollPaddingBottom = "" }
  }, [])
  return (
    <footer
      ref={ref}
      data-slot="sticky-action-bar"
      className={cn(
        "sticky bottom-0 z-10 flex min-h-14 flex-wrap items-center justify-between gap-x-4 gap-y-2 border-t bg-background px-4 py-1.5 [@media(max-height:480px)]:static",
        className,
      )}
      {...props}
    >
      <div className="min-w-0">{status}</div>
      <div className="ms-auto flex flex-wrap items-center gap-2">{children}</div>
    </footer>
  )
}

export { StickyActionBar }
