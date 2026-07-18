import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { emptyProfile } from '../demoData';
import { OnboardingPage } from '../pages/ProfilePages';
import { renderWithApp } from './render';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('new-user onboarding', () => {
  it('keeps contacts in the draft, records location review, and persists everything only at final approval', async () => {
    vi.spyOn(api, 'getCurrentUser').mockResolvedValue({ authenticated: true });
    vi.spyOn(api, 'getProfile').mockResolvedValue(emptyProfile);
    const updateProfile = vi.spyOn(api, 'updateProfile').mockImplementation((profile) => Promise.resolve(profile));
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })));
    const user = userEvent.setup();
    renderWithApp(<OnboardingPage />, '/onboarding');

    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.type(screen.getByLabelText('Your name'), 'Prepared Patient');
    await user.type(screen.getByLabelText('Date of birth'), '1990-04-03');
    await user.type(screen.getByLabelText('Blood group (optional)'), 'O+');
    await user.type(screen.getByLabelText('Preferred hospital'), 'Fictional Community Hospital');
    await user.type(screen.getByLabelText('Doctor contact'), 'Dr Test · +910000000000');
    await user.type(screen.getByLabelText('Insurance details'), 'Private test policy');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    await user.type(screen.getByLabelText('Allergies'), 'Penicillin');
    await user.type(screen.getByLabelText('Medical conditions'), 'Asthma');
    await user.type(screen.getByLabelText('Current medicines'), 'Test inhaler');
    await user.type(screen.getByLabelText('Relevant procedures'), 'Appendectomy, 2020');
    await user.click(screen.getByRole('checkbox', { name: /^I listed my allergies/ }));
    await user.click(screen.getByRole('checkbox', { name: /^I listed my current medicines/ }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    await user.type(screen.getByLabelText('Your name'), 'Emergency Contact');
    await user.type(screen.getByLabelText('Relationship'), 'Sibling');
    await user.type(screen.getByLabelText('Phone number'), '+919876543210');
    await user.click(screen.getByRole('button', { name: 'Add contact' }));
    expect(screen.getByText('Emergency Contact')).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Add contact' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(screen.getByRole('checkbox', { name: 'Name' }));
    await user.click(screen.getByRole('checkbox', { name: /^I reviewed emergency location sharing on this device\./ }));
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(screen.getByText('O+')).toBeVisible();
    expect(screen.getByText('Fictional Community Hospital')).toBeVisible();
    expect(screen.getByText('Dr Test · +910000000000')).toBeVisible();
    expect(screen.getByText('Private test policy')).toBeVisible();
    expect(screen.getByText('Penicillin')).toBeVisible();
    expect(screen.getByText('Asthma')).toBeVisible();
    expect(screen.getByText('Test inhaler')).toBeVisible();
    expect(screen.getByText('Appendectomy, 2020')).toBeVisible();
    expect(screen.getByText('Emergency Contact · Sibling · +919876543210 · Not verified')).toBeVisible();
    expect(screen.getAllByText('Approved to share during an active emergency')).toHaveLength(1);
    expect(screen.getAllByText('Not approved to share')).toHaveLength(5);
    expect(screen.getByText('Reviewed; permission is still requested before sharing')).toBeVisible();
    await user.click(screen.getByLabelText('I confirm this information is accurate to the best of my knowledge'));
    await user.click(screen.getByRole('button', { name: 'Finish setup' }));

    await waitFor(() => expect(updateProfile).toHaveBeenCalledTimes(1));
    const [submitted, options] = updateProfile.mock.calls[0]!;
    expect(submitted).toEqual(expect.objectContaining({
      name: 'Prepared Patient',
      bloodGroup: 'O+',
      preferredHospital: 'Fictional Community Hospital',
      doctor: 'Dr Test · +910000000000',
      insurance: 'Private test policy',
      allergies: ['Penicillin'],
      conditions: ['Asthma'],
      medicines: ['Test inhaler'],
      procedures: ['Appendectomy, 2020'],
      shareFields: ['name'],
      locationPermissionReviewed: true,
      contacts: [expect.objectContaining({ name: 'Emergency Contact', relationship: 'Sibling', phone: '+919876543210', verified: false })],
    }));
    expect(options).toEqual({ markReviewed: true });
  }, 30_000);
});
