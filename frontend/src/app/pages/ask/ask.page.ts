import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AskResponse, SearchHit } from '../../core/api.models';
import { RagApiService } from '../../core/rag-api.service';

type Language = 'ar' | 'en';
type BusyAction = 'ingest' | 'ask' | 'search' | null;

interface Notice {
  kind: 'success' | 'error';
  message: string;
}

const COPY = {
  ar: {
    switchLanguage: 'Switch language to English',
    languageButton: 'English',
    serviceOnline: 'الخدمة متصلة',
    serviceChecking: 'جاري فحص الخدمة',
    serviceOffline: 'الخدمة غير متصلة',
    eyebrow: 'مساعد سياسات الموارد البشرية · RAG محلي',
    title: 'اسأل السياسة،',
    titleHighlight: 'والإجابة بمصدرها.',
    intro:
      'أضف مستندات الموارد البشرية، وابحث في محتواها، واحصل على إجابة مرتبطة بالمقاطع التي استندت إليها.',
    localModels: 'النماذج تعمل محلياً',
    database: 'قاعدة المعرفة',
    addPolicy: 'أضف سياسة',
    addPolicyDescription:
      'الصق نص السياسة. سنقسمه إلى مقاطع ونحفظ embedding لكل مقطع للبحث لاحقاً.',
    policyName: 'اسم السياسة',
    policyText: 'نص السياسة',
    indexing: 'جاري الفهرسة...',
    indexPolicy: 'فهرسة السياسة',
    documentId: 'معرّف المستند',
    semanticSearch: 'بحث دلالي وإجابة',
    askPolicy: 'اسأل عن سياسة',
    askDescription: 'اكتب سؤالك بلغة طبيعية. سيبحث النظام في المقاطع الأقرب لمعناه.',
    yourQuestion: 'سؤالك',
    resultCount: 'عدد المقاطع',
    oneChunk: 'مقطع واحد',
    threeChunks: '3 مقاطع',
    fiveChunks: '5 مقاطع',
    searching: 'جاري البحث...',
    searchOnly: 'بحث فقط',
    asking: 'جاري السؤال...',
    askAssistant: 'اسأل المساعد',
    answer: 'الإجابة',
    sources: 'المصادر المستخدمة',
    closestChunks: 'أقرب المقاطع',
    walkthrough: 'من النص إلى الإجابة',
    behindScenes: 'إيه اللي بيحصل وراء الكواليس؟',
    detailedGuide: 'شرح تفصيلي للمشروع',
    chunking: 'تقسيم السياسة عند العناوين والفقرات إلى مقاطع صغيرة مفهومة.',
    embedding: 'تحويل كل مقطع إلى متجه أرقام يلتقط معناه باستخدام nomic-embed-text.',
    retrieval: 'تحويل السؤال لمتجه واختيار المقاطع الأقرب دلالياً باستخدام cosine similarity.',
    answerStep: 'إرسال السؤال والمصادر إلى llama3.2:3b لصياغة إجابة مرتبطة بالدليل.',
    storageNote:
      'المقاطع والمتجهات تُحفظ في SQL Server. تغيير نموذج embeddings يتطلب إعادة فهرسة السياسات.',
    footer: 'نموذج تعليمي يعمل محلياً عبر Ollama',
    ingestSuccess: 'تم حفظ السياسة وتقسيمها وفهرسة مقاطعها.',
    questionRequired: 'اكتب سؤالك أولاً.',
    searchQuestionRequired: 'اكتب سؤالاً للبحث.',
    policyRequired: 'اكتب اسم السياسة ونصها أولاً.',
    genericError: 'حصل خطأ أثناء الاتصال بالخدمة. تأكد أن الـAPI شغال.',
    helpful: 'هل كانت الإجابة مفيدة؟',
    thanks: 'شكراً على تقييمك.',
  },
  en: {
    switchLanguage: 'Switch language to Arabic',
    languageButton: 'العربية',
    serviceOnline: 'API connected',
    serviceChecking: 'Checking API',
    serviceOffline: 'API disconnected',
    eyebrow: 'HR POLICY ASSISTANT · LOCAL RAG',
    title: 'Ask the policy,',
    titleHighlight: 'see the evidence.',
    intro:
      'Add HR documents, search their contents, and get answers linked to the policy passages they use.',
    localModels: 'Models run locally',
    database: 'KNOWLEDGE BASE',
    addPolicy: 'Add a policy',
    addPolicyDescription:
      'Paste policy text. We split it into chunks and store an embedding for semantic search.',
    policyName: 'Policy name',
    policyText: 'Policy text',
    indexing: 'Indexing policy...',
    indexPolicy: 'Index policy',
    documentId: 'Document ID',
    semanticSearch: 'SEMANTIC SEARCH & ANSWERS',
    askPolicy: 'Ask about a policy',
    askDescription:
      'Ask in natural language. The system finds policy chunks with the closest meaning.',
    yourQuestion: 'Your question',
    resultCount: 'Number of chunks',
    oneChunk: '1 chunk',
    threeChunks: '3 chunks',
    fiveChunks: '5 chunks',
    searching: 'Searching...',
    searchOnly: 'Search only',
    asking: 'Generating answer...',
    askAssistant: 'Ask assistant',
    answer: 'Answer',
    sources: 'Sources used',
    closestChunks: 'Closest chunks',
    walkthrough: 'FROM POLICY TEXT TO ANSWER',
    behindScenes: 'What happens behind the scenes?',
    detailedGuide: 'Read the project guide',
    chunking: 'Split policy text at headings and paragraphs into small, meaningful chunks.',
    embedding: 'Turn each chunk into a numeric vector with nomic-embed-text.',
    retrieval: 'Embed the question and find the closest chunks with cosine similarity.',
    answerStep: 'Send the question and retrieved evidence to llama3.2:3b for a grounded answer.',
    storageNote:
      'Chunks and vectors are stored in SQL Server. Changing the embedding model requires reindexing policies.',
    footer: 'Educational prototype running locally with Ollama',
    ingestSuccess: 'Policy saved, chunked, and indexed successfully.',
    questionRequired: 'Enter a question first.',
    searchQuestionRequired: 'Enter a question to search.',
    policyRequired: 'Enter both a policy name and its text.',
    genericError: 'The request failed. Check that the API is running.',
    helpful: 'Was this answer helpful?',
    thanks: 'Thanks for your feedback.',
  },
} as const;

@Component({
  selector: 'app-ask',
  imports: [FormsModule],
  templateUrl: './ask.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AskPage {
  private readonly samples = {
    ar: {
      name: 'سياسة الإجازة السنوية',
      text: [
        '# سياسة الموارد البشرية',
        '',
        '## الإجازة السنوية',
        '',
        'الموظف الذي لديه خبرة من سنة إلى 5 سنوات يستحق 21 يوم إجازة سنوية.',
        '',
        'الموظف الذي لديه خبرة أكثر من 5 سنوات يستحق 30 يوم إجازة سنوية.',
      ].join('\n'),
      question: 'كم يوم إجازة سنوية لموظف لديه 3 سنوات خبرة؟',
    },
    en: {
      name: 'Annual Leave Policy',
      text: [
        '# Human Resources Policy',
        '',
        '## Annual Leave',
        '',
        'Employees with 1 to 5 years of experience receive 21 days of annual leave.',
        '',
        'Employees with more than 5 years of experience receive 30 days of annual leave.',
      ].join('\n'),
      question: 'How many annual leave days does an employee with 3 years of experience get?',
    },
  };

  readonly language = signal<Language>('ar');
  readonly ui = computed(() => COPY[this.language()]);
  readonly apiStatus = signal<'checking' | 'online' | 'offline'>('checking');
  readonly busyAction = signal<BusyAction>(null);
  readonly notice = signal<Notice | null>(null);
  readonly documentId = signal<string | null>(null);
  readonly answer = signal<AskResponse | null>(null);
  readonly searchHits = signal<SearchHit[]>([]);
  readonly feedbackSent = signal(false);

  policyName = this.samples.ar.name;
  policyText = this.samples.ar.text;
  question = this.samples.ar.question;
  topK = 3;

  constructor(private readonly ragApi: RagApiService) {
    this.checkApi();
  }

  toggleLanguage(): void {
    const current = this.language();
    const next: Language = current === 'ar' ? 'en' : 'ar';
    const currentSample = this.samples[current];
    const nextSample = this.samples[next];

    if (this.policyName === currentSample.name) {
      this.policyName = nextSample.name;
    }
    if (this.policyText === currentSample.text) {
      this.policyText = nextSample.text;
    }
    if (this.question === currentSample.question) {
      this.question = nextSample.question;
    }

    this.language.set(next);
  }

  ingestPolicy(): void {
    if (!this.policyName.trim() || !this.policyText.trim()) {
      this.showError(this.ui().policyRequired);
      return;
    }

    this.busyAction.set('ingest');
    this.notice.set(null);

    this.ragApi.ingestText(this.policyName, this.policyText).subscribe({
      next: (result) => {
        this.documentId.set(result.documentId);
        this.notice.set({ kind: 'success', message: this.ui().ingestSuccess });
      },
      error: (error) => this.showError(error),
      complete: () => this.busyAction.set(null),
    });
  }

  askPolicy(): void {
    if (!this.question.trim()) {
      this.showError(this.ui().questionRequired);
      return;
    }

    this.busyAction.set('ask');
    this.notice.set(null);
    this.searchHits.set([]);

    this.ragApi.ask(this.question, this.topK).subscribe({
      next: (result) => {
        this.feedbackSent.set(false);
        this.answer.set(result);
      },
      error: (error) => this.showError(error),
      complete: () => this.busyAction.set(null),
    });
  }

  searchPolicies(): void {
    if (!this.question.trim()) {
      this.showError(this.ui().searchQuestionRequired);
      return;
    }

    this.busyAction.set('search');
    this.notice.set(null);
    this.answer.set(null);

    this.ragApi.search(this.question, this.topK).subscribe({
      next: (hits) => this.searchHits.set(hits),
      error: (error) => this.showError(error),
      complete: () => this.busyAction.set(null),
    });
  }

  sendFeedback(helpful: boolean): void {
    const id = this.answer()?.questionId;
    if (!id) {
      return;
    }
    this.ragApi.feedback(id, helpful).subscribe({
      next: () => this.feedbackSent.set(true),
      error: (error) => this.showError(error),
    });
  }

  formatScore(score: number): string {
    return Math.round(score * 100).toString() + '%';
  }

  guideUrl(): string {
    return this.language() === 'ar' ? 'ARCHITECTURE_AR.md' : 'ARCHITECTURE_EN.md';
  }

  private checkApi(): void {
    this.ragApi.health().subscribe({
      next: () => this.apiStatus.set('online'),
      error: () => this.apiStatus.set('offline'),
    });
  }

  private showError(error: unknown): void {
    const response = error instanceof HttpErrorResponse ? error.error : null;
    let message: string = this.ui().genericError;

    if (typeof response === 'string') {
      message = response;
    } else if (response && typeof response === 'object') {
      const body = response as Record<string, unknown>;
      const text = body['detail'] ?? body['message'] ?? body['title'];
      if (typeof text === 'string') {
        message = text;
      }
    } else if (error instanceof Error) {
      message = error.message;
    }

    this.notice.set({ kind: 'error', message });
    this.busyAction.set(null);
  }
}
