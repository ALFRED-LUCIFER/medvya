// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import axe from 'axe-core';
import { Bell, Users } from 'lucide-react';
import { AccountMenu, Button, NavRailItem, PageHeader, Tooltip } from './index';

// MDV-P1-084. Radix menus need pointer capture, which jsdom lacks; Playwright drives the real keyboard path on /__ui.
Object.assign(Element.prototype, { hasPointerCapture: () => false, releasePointerCapture: () => {}, scrollIntoView: () => {} });
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
afterEach(cleanup);

describe('AccountMenu', () => {
  const props = { name: 'Meera Iyer', roleLabel: 'Doctor', onLocaleChange: vi.fn(), onMyDevices: vi.fn(), onSignOut: vi.fn() };
  const openMenu = () => fireEvent.keyDown(screen.getByRole('button', { name: 'Signed in as Meera Iyer' }), { key: 'Enter' });

  it('lists language, my devices, change facility and sign out in that order, sign out last after a separator', () => {
    render(<AccountMenu {...props} onChangeFacility={vi.fn()} />);
    openMenu();
    const menu = screen.getByRole('menu');
    expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual(['Language', 'My devices', 'Change facility', 'Sign out']);
    const last = menu.lastElementChild!;
    expect(last.textContent).toBe('Sign out');
    expect(last.previousElementSibling!.getAttribute('role')).toBe('separator');
    expect(menu.textContent).toContain('Meera Iyer');
    expect(menu.textContent).toContain('Doctor');
    for (const i of screen.getAllByRole('menuitem')) expect(i.className.split(/\s+/)).toContain('min-h-11');
  });

  it('hides change facility for one facility, and calls the handler for the chosen item', () => {
    render(<AccountMenu {...props} />);
    openMenu();
    expect(screen.queryByRole('menuitem', { name: 'Change facility' })).toBeNull();
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }));
    expect(props.onSignOut).toHaveBeenCalledOnce();
  });
});

describe('NavRailItem', () => {
  it('shows its label as visible text under the icon, never only as a tooltip', async () => {
    const { container } = render(<nav aria-label="Main"><NavRailItem href="/patients" icon={Users} label="Patients" aria-current="page" /></nav>);
    const link = screen.getByRole('link', { name: 'Patients' });
    expect(link.querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
    expect(link.querySelector('span')!.textContent).toBe('Patients');
    expect(link.querySelector('span')!.className).not.toMatch(/sr-only|truncate/);
    expect((await axe.run(container)).violations).toEqual([]);
  });

  it('renders inside the caller\'s link element with asChild', () => {
    render(<NavRailItem asChild icon={Users} label="Patients"><a href="/p" data-router="yes" /></NavRailItem>);
    const link = screen.getByRole('link', { name: 'Patients' });
    expect(link.getAttribute('data-router')).toBe('yes');
    expect(link.getAttribute('data-slot')).toBe('nav-rail-item');
  });
});

describe('PageHeader', () => {
  it('keeps its props and adds back and meta slots in the one row', () => {
    render(<PageHeader title="Front desk" back={<a href="/">Back</a>} meta="Fri 13 Nov" actions={<button>Walk-in</button>} description="Today" />);
    const row = document.querySelector('[data-slot=page-header-row]')!;
    expect(row.className.split(/\s+/)).toContain('min-h-header');
    expect(row.textContent).toBe('BackFront deskFri 13 NovWalk-in');
    expect(screen.getByRole('heading', { level: 1 }).className).toContain('text-title');
    expect(screen.getByText('Today')).toBeTruthy();
  });
});

describe('Tooltip', () => {
  it('shows on keyboard focus while the button keeps its own name', async () => {
    render(<Tooltip content="Notifications"><Button size="icon" aria-label="Notifications"><Bell aria-hidden /></Button></Tooltip>);
    const button = screen.getByRole('button', { name: 'Notifications' });
    fireEvent.focus(button);
    expect((await screen.findByRole('tooltip')).textContent).toBe('Notifications');
    expect(button.getAttribute('aria-label')).toBe('Notifications');
  });
});
