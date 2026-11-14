import * as React from "react"
import { cn } from "#lib/utils"

/** A load that ends sooner shows no placeholder at all, so a fast page never flashes (MDV-P2-056). */
export const skeletonDelayMs = 200

/**
 * A placeholder block in the page's final layout (MDV-P2-056, DESIGN.md Motion): it takes its space from the first frame, so nothing
 * jumps when the data comes, but stays invisible for 200 ms; then an opacity pulse (1 → 0.6 → 1 over 1,200 ms), static blocks under
 * reduced motion (styles.css). Children are hidden and shown with it. Decorative: the page says "Loading…" once, in a SkeletonGroup.
 */
function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  const [shown, setShown] = React.useState(false)
  React.useEffect(() => {
    const timer = setTimeout(() => setShown(true), skeletonDelayMs)
    return () => clearTimeout(timer)
  }, [])
  return (
    <div
      data-slot="skeleton"
      data-shown={shown || undefined}
      aria-hidden
      className={cn("rounded-md bg-muted", shown ? "motion-safe:animate-skeleton" : "invisible", className)}
      {...props}
    />
  )
}

/** Skeleton blocks with the page's one polite "Loading…" for screen readers. */
function SkeletonGroup({ label, className, children, ...props }: React.ComponentProps<"div"> & { label: string }) {
  return (
    <div data-slot="skeleton-group" role="status" className={className} {...props}>
      <span className="sr-only">{label}</span>
      {children}
    </div>
  )
}

export { Skeleton, SkeletonGroup }
