import * as React from "react"
import { cn } from "#lib/utils"
import { Button } from "#components/ui/button"

export type SignaturePadProps = {
  /** What the pad is for, read by assistive technology ("Patient's signature"). */
  label: string
  /** The words of the clear button. */
  clearLabel: string
  /** Shown under the pad, e.g. "Sign with a finger or a stylus". */
  hint: string
  /** The drawn signature as a PNG data URL after each stroke, or null once cleared. */
  onChange: (png: string | null) => void
  className?: string
}

/**
 * A signature drawn with a finger, stylus or mouse (MDV-P8-019, procedure consent on a tablet). The pad is paper-like in every theme (black ink on white),
 * so the PNG it gives prints as signed. Pointer events cover touch, pen and mouse; scrolling is off over the pad so a stroke never moves the page. The
 * canvas is named by its label and the hint; the form beside it carries the signer's name in words, so the signature is never the only record.
 */
function SignaturePad({ label, clearLabel, hint, onChange, className }: SignaturePadProps) {
  const canvas = React.useRef<HTMLCanvasElement>(null)
  const drawing = React.useRef(false)
  const hintId = React.useId()
  const [empty, setEmpty] = React.useState(true)

  const context = () => {
    const c = canvas.current
    const ctx = c?.getContext("2d")
    if (!c || !ctx) return null
    // Size the drawing surface to the element at the device's pixel ratio the first time, white like paper.
    const ratio = window.devicePixelRatio || 1
    if (c.width !== Math.round(c.clientWidth * ratio)) {
      c.width = Math.round(c.clientWidth * ratio)
      c.height = Math.round(c.clientHeight * ratio)
      ctx.scale(ratio, ratio)
      ctx.fillStyle = "white"
      ctx.fillRect(0, 0, c.clientWidth, c.clientHeight)
    }
    ctx.lineWidth = 2.5
    ctx.lineCap = "round"
    ctx.lineJoin = "round"
    ctx.strokeStyle = "black"
    return ctx
  }

  const point = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const box = e.currentTarget.getBoundingClientRect()
    return { x: e.clientX - box.left, y: e.clientY - box.top }
  }

  const start = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const ctx = context()
    if (!ctx) return
    e.currentTarget.setPointerCapture?.(e.pointerId)
    drawing.current = true
    const { x, y } = point(e)
    ctx.beginPath()
    ctx.moveTo(x, y)
    ctx.lineTo(x + 0.1, y + 0.1) // a tap leaves a dot
    ctx.stroke()
  }

  const move = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return
    const ctx = context()
    if (!ctx) return
    const { x, y } = point(e)
    ctx.lineTo(x, y)
    ctx.stroke()
  }

  const end = () => {
    if (!drawing.current) return
    drawing.current = false
    setEmpty(false)
    onChange(canvas.current?.toDataURL("image/png") ?? null)
  }

  const clear = () => {
    const c = canvas.current
    const ctx = c?.getContext("2d")
    if (c && ctx) {
      ctx.fillStyle = "white"
      ctx.fillRect(0, 0, c.width, c.height)
    }
    setEmpty(true)
    onChange(null)
  }

  return (
    <div className={cn("flex flex-col gap-2", className)} data-slot="signature-pad">
      <canvas
        ref={canvas}
        role="img"
        aria-label={label}
        aria-describedby={hintId}
        className="h-40 w-full touch-none rounded-md border-2 border-dashed border-border bg-white"
        onPointerDown={start}
        onPointerMove={move}
        onPointerUp={end}
        onPointerCancel={end}
        onPointerLeave={end}
      />
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p id={hintId} className="text-caption text-muted-foreground">{hint}</p>
        <Button type="button" variant="ghost" disabled={empty} onClick={clear}>{clearLabel}</Button>
      </div>
    </div>
  )
}

export { SignaturePad }
