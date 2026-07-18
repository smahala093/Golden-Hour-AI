import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { demoProfile } from '../demoData';
import { ContactVerificationPanel } from '../pages/ProfilePages';

afterEach(() => vi.restoreAllMocks());

describe('contact verification', () => {
  it('labels the development code as mock without claiming SMS delivery and refreshes after confirmation', async () => {
    const contact = { ...demoProfile.contacts[1]!, id: 'e6f34a8b-23ac-47e1-b84b-f41593e5d630', verified: false };
    const verifiedProfile = { ...demoProfile, contacts: [{ ...contact, verified: true }] };
    vi.spyOn(api, 'requestContactVerification').mockResolvedValue({
      challenge: 'bounded-test-challenge', expiresAtUtc: '2026-07-18T10:05:00.000Z', status: 'mock-no-delivery', developmentCode: '654321',
    });
    vi.spyOn(api, 'confirmContactVerification').mockResolvedValue();
    vi.spyOn(api, 'getProfile').mockResolvedValue(verifiedProfile);
    const updated = vi.fn();
    const user = userEvent.setup();

    render(<ContactVerificationPanel contact={contact} onProfileUpdated={updated} />);
    await user.click(screen.getByRole('button', { name: 'Request verification code' }));

    expect(screen.getByText('Mock development code:').closest('p')).toHaveTextContent('Mock development code: 654321');
    expect(screen.getByText('No SMS was sent in mock mode.')).toBeVisible();
    expect(screen.getByText('A verification challenge was created. Delivery has not been confirmed.')).toBeVisible();

    await user.type(screen.getByLabelText('Verification code'), '654321');
    await user.click(screen.getByRole('button', { name: 'Confirm code' }));

    expect(api.confirmContactVerification).toHaveBeenCalledWith(contact.id, 'bounded-test-challenge', '654321');
    expect(updated).toHaveBeenCalledWith(verifiedProfile);
  });
});
