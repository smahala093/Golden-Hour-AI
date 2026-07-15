import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from '../components/ErrorBoundary';

const privateFailure = 'Patient Jane Example takes SecretMedicine 40mg';

function CrashingChild(): never {
  throw new Error(privateFailure);
}

afterEach(() => vi.restoreAllMocks());

describe('ErrorBoundary', () => {
  it('fails safely without rendering raw errors or private content', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    render(<ErrorBoundary><CrashingChild /></ErrorBoundary>);

    expect(screen.getByRole('heading', { name: 'Something went wrong' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Call 112' })).toHaveAttribute('href', 'tel:112');
    expect(screen.getByRole('button', { name: 'Reload page' })).toBeVisible();
    expect(screen.queryByText(privateFailure)).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent('SecretMedicine');
  });
});
