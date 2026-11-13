import * as React from "react"
import { cn } from "#lib/utils"
import { Switch as SwitchPrimitive } from "radix-ui"

// The button is a 44 px target around the 32 px track; the negative margins keep the layout the size of the track (MDV-P1-077).
function Switch({ className, ...props }: React.ComponentProps<typeof SwitchPrimitive.Root>) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      className={cn(
        "peer group/switch -mx-1.5 -my-[0.8rem] inline-flex size-11 shrink-0 items-center justify-center rounded-full outline-none disabled:cursor-not-allowed",
        className
      )}
      {...props}
    >
      <span
        data-slot="switch-track"
        className="inline-flex h-[1.15rem] w-8 items-center rounded-full border border-transparent shadow-xs transition-colors group-focus-visible/switch:border-ring group-focus-visible/switch:ring-[3px] group-focus-visible/switch:ring-ring/50 group-disabled/switch:opacity-50 group-data-[state=checked]/switch:bg-primary group-data-[state=unchecked]/switch:bg-input dark:group-data-[state=unchecked]/switch:bg-input/80"
      >
        <SwitchPrimitive.Thumb
          data-slot="switch-thumb"
          className="pointer-events-none block size-4 rounded-full bg-background ring-0 transition-transform data-[state=checked]:translate-x-[calc(100%-2px)] rtl:data-[state=checked]:-translate-x-[calc(100%-2px)] data-[state=unchecked]:translate-x-0 dark:data-[state=checked]:bg-primary-foreground dark:data-[state=unchecked]:bg-foreground"
        />
      </span>
    </SwitchPrimitive.Root>
  )
}

export { Switch }
