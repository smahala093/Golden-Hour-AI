import { describe, expect, it } from 'vitest';
import { demoExtraction } from '../demoData';
import { incidentExtractionSchema } from '../types';

describe('incident extraction trust boundary', () => {
  it('accepts the bounded demonstration extraction', () => {
    expect(incidentExtractionSchema.safeParse(demoExtraction).success).toBe(true);
  });

  it('rejects more than three critical questions', () => {
    const result = incidentExtractionSchema.safeParse({
      ...demoExtraction,
      criticalMissingQuestions: Array.from({ length: 4 }, (_, index) => ({
        id: `question-${index}`,
        question: 'Fictional bounded question?',
        answerType: 'yes_no',
      })),
    });

    expect(result.success).toBe(false);
  });

  it('rejects unrecognized model-controlled fields', () => {
    const result = incidentExtractionSchema.safeParse({
      ...demoExtraction,
      inventedTreatmentInstruction: 'Do something not present in a reviewed protocol',
    });

    expect(result.success).toBe(false);
  });
});
