import * as React from "react"
import { locales, useI18n, type Locale } from "@medvya/i18n"
import { Label } from "#components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "#components/ui/select"

/** The UI language picker; each language is named in itself. */
function LanguageSwitcher({ value, onChange, inline = false }: { value: Locale; onChange: (l: Locale) => void; inline?: boolean }) {
  const { t } = useI18n()
  const id = React.useId()
  return (
    <div className={inline ? "flex items-center gap-2" : "flex flex-col gap-1.5"}>
      <Label htmlFor={id}>{t("app.language")}</Label>
      <Select value={value} onValueChange={v => onChange(v as Locale)}>
        <SelectTrigger id={id} className="min-w-40"><SelectValue /></SelectTrigger>
        <SelectContent>
          {locales.map(l => <SelectItem key={l.code} value={l.code} lang={l.code}>{l.label}</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
  )
}

export { LanguageSwitcher }
