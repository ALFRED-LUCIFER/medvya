import * as React from "react"
import { Tooltip as TooltipPrimitive } from "radix-ui"
import { cn } from "#lib/utils"

type TooltipProps = Omit<React.ComponentProps<typeof TooltipPrimitive.Content>, "content"> & {
  /** The tip text. The trigger must still carry its own accessible name (aria-label): a tooltip is never the only label. */
  content: React.ReactNode
  /** One focusable element, usually an icon Button. */
  children: React.ReactElement
}

/** A tip on hover and on keyboard focus, for icon-only buttons. Never the only label for navigation (ruling 8). */
function Tooltip({ content, children, className, sideOffset = 4, ...props }: TooltipProps) {
  return (
    <TooltipPrimitive.Provider delayDuration={300}>
      <TooltipPrimitive.Root>
        <TooltipPrimitive.Trigger asChild>{children}</TooltipPrimitive.Trigger>
        <TooltipPrimitive.Portal>
          <TooltipPrimitive.Content
            data-slot="tooltip-content"
            sideOffset={sideOffset}
            className={cn(
              "z-50 w-fit max-w-panel rounded-md bg-foreground px-2 py-1 text-caption text-background shadow-popover",
              "data-[state=delayed-open]:animate-in data-[state=delayed-open]:fade-in-0 data-[state=instant-open]:animate-in data-[state=instant-open]:fade-in-0",
              className
            )}
            {...props}
          >
            {content}
          </TooltipPrimitive.Content>
        </TooltipPrimitive.Portal>
      </TooltipPrimitive.Root>
    </TooltipPrimitive.Provider>
  )
}

export { Tooltip, type TooltipProps }
