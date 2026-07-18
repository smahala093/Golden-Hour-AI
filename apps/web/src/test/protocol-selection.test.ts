import { describe, expect, it } from 'vitest';
import { demoExtraction } from '../demoData';
import { protocolId, questionsForConfirmation } from '../pages/EmergencyFlowPages';
import { incidentExtractionSchema } from '../types';

describe('reviewed protocol selection', () => {
  it('selects the not-breathing reviewed protocol only when breathing is explicitly no', () => {
    expect(protocolId('unconscious', 'no')).toBe('unconscious-not-breathing');
  });

  it('selects the breathing reviewed protocol only when breathing is explicitly yes', () => {
    expect(protocolId('unconscious', 'yes')).toBe('unconscious-breathing');
  });

  it('fails closed to the unknown emergency protocol when breathing is uncertain', () => {
    expect(protocolId('unconscious', 'unknown')).toBe('unknown-emergency');
  });

  it('adds the allowlisted interpretation confirmation without exceeding three questions', () => {
    const questions = questionsForConfirmation(demoExtraction, true);

    expect(questions).toHaveLength(3);
    expect(questions.at(-1)).toEqual(expect.objectContaining({ id: 'confirm-facts', answerType: 'yes_no' }));
  });

  it('accepts bounded relative symptom time but rejects duplicate server-owned question IDs', () => {
    const relativeTime = { ...demoExtraction, reportedSymptomStartTime: 'about 10 minutes ago', criticalMissingQuestions: [] };
    expect(incidentExtractionSchema.safeParse(relativeTime).success).toBe(true);

    const duplicateQuestions = {
      ...relativeTime,
      criticalMissingQuestions: [
        { id: 'conscious', question: 'Is the person conscious?', answerType: 'yes_no' },
        { id: 'conscious', question: 'Is the person conscious?', answerType: 'yes_no' },
      ],
    };
    expect(incidentExtractionSchema.safeParse(duplicateQuestions).success).toBe(false);
  });
});
