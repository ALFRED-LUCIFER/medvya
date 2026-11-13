import { Building2, Languages, LogOut, MonitorSmartphone } from "lucide-react"
import { locales, useI18n, type Locale } from "@medvya/i18n"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuRadioGroup, DropdownMenuRadioItem,
  DropdownMenuSeparator, DropdownMenuSub, DropdownMenuSubContent, DropdownMenuSubTrigger, DropdownMenuTrigger,
} from "#components/ui/dropdown-menu"

type AccountMenuProps = {
  name: string
  /** Already translated, e.g. t(`role.${role}`). */
  roleLabel: string
  onLocaleChange: (locale: Locale) => void
  onMyDevices: () => void
  /** Leave out when the user has one facility: the item is then not shown. */
  onChangeFacility?: () => void
  onSignOut: () => void
}

const initials = (name: string) => name.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]).join("").toUpperCase()

/** The avatar button and its menu: name and role, language, my devices, change facility, and sign out last and separated (DESIGN.md Navigation). */
function AccountMenu({ name, roleLabel, onLocaleChange, onMyDevices, onChangeFacility, onSignOut }: AccountMenuProps) {
  const { t, locale, dir } = useI18n()
  return (
    <DropdownMenu dir={dir}>
      <DropdownMenuTrigger
        className="grid size-11 shrink-0 place-items-center rounded-full bg-brand-700 text-label text-white hover:bg-brand-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        {/* Named by content, not aria-label, so the visible initials are not a label/name mismatch (WCAG 2.5.3). */}
        <span aria-hidden>{initials(name)}</span>
        <span className="sr-only">{t("session.signedInAs", { name })}</span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuLabel className="flex flex-col">
          <span className="text-label">{name}</span>
          <span className="text-caption text-muted-foreground">{roleLabel}</span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuSub>
          <DropdownMenuSubTrigger><Languages aria-hidden />{t("app.language")}</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup value={locale} onValueChange={v => onLocaleChange(v as Locale)}>
              {locales.map(l => <DropdownMenuRadioItem key={l.code} value={l.code} lang={l.code}>{l.label}</DropdownMenuRadioItem>)}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuItem onSelect={onMyDevices}><MonitorSmartphone aria-hidden />{t("myDevices.link")}</DropdownMenuItem>
        {onChangeFacility && <DropdownMenuItem onSelect={onChangeFacility}><Building2 aria-hidden />{t("session.switchFacility")}</DropdownMenuItem>}
        <DropdownMenuSeparator />
        <DropdownMenuItem onSelect={onSignOut}><LogOut aria-hidden />{t("session.signOut")}</DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

export { AccountMenu, type AccountMenuProps }
