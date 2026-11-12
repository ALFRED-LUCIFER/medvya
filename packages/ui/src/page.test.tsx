// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { PageContainer, PageHeader } from './index';

afterEach(cleanup);

describe('PageContainer', () => {
  it('is fluid by default and has no width cap', () => {
    render(<PageContainer data-testid="c">x</PageContainer>);
    expect(screen.getByTestId('c').className).toContain('max-w-none');
  });
  it.each([['form', 'max-w-3xl'], ['reading', 'max-w-6xl']] as const)('caps the %s width', (width, cls) => {
    render(<PageContainer width={width} data-testid="c">x</PageContainer>);
    expect(screen.getByTestId('c').className).toContain(cls);
  });
});

describe('PageHeader', () => {
  it('renders one h1 with the title, description and actions', () => {
    render(<PageHeader title="Patients" description="Find a patient" actions={<button>Add</button>} />);
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1);
    expect(screen.getByRole('heading', { name: 'Patients' })).toBeTruthy();
    expect(screen.getByText('Find a patient')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Add' })).toBeTruthy();
  });
});
