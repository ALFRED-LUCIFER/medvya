import * as React from "react"
import { Popover as PopoverPrimitive } from "radix-ui"
import { cn } from "#lib/utils"

// A panel anchored to a control (shadcn popover, trimmed). Radix closes it on Escape and on a click outside, and a Select opened inside it
// stays inside its layer. No motion: the patient search drops down in place (DESIGN.md Motion, "never animate the Ctrl+K search").
function Popover(props: React.ComponentProps<typeof PopoverPrimitive.Root>) {
  return <PopoverPrimitive.Root data-slot="popover" {...props} />
}

function PopoverAnchor(props: React.ComponentProps<typeof PopoverPrimitive.Anchor>) {
  return <PopoverPrimitive.Anchor data-slot="popover-anchor" {...props} />
}

function PopoverContent({ className, align = "start", sideOffset = 4, ...props }: React.ComponentProps<typeof PopoverPrimitive.Content>) {
  return (
    <PopoverPrimitive.Portal>
      <PopoverPrimitive.Content
        data-slot="popover-content"
        align={align}
        sideOffset={sideOffset}
        className={cn("z-50 max-h-(--radix-popover-content-available-height) overflow-y-auto rounded-md border bg-card p-3 text-card-foreground shadow-popover outline-none", className)}
        {...props}
      />
    </PopoverPrimitive.Portal>
  )
}

export { Popover, PopoverAnchor, PopoverContent }
