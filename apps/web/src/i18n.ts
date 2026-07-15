import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';

const en = {
  common: {
    appName: 'Golden Hour AI',
    tagline: 'Turn panic into coordinated action.',
    call: 'Call {{number}}',
    callEmergency: 'Call emergency services',
    cancel: 'Cancel',
    continue: 'Continue',
    back: 'Back',
    save: 'Save changes',
    done: 'Done',
    yes: 'Yes',
    no: 'No',
    unknown: 'Not sure',
    loading: 'Loading…',
    retry: 'Try again',
    demoLabel: 'Demonstration only',
    protocolDisclaimer: 'Demonstration guidance requiring clinical review before production use.',
    selfReported: 'Self-reported information',
    stale: 'Saved offline · may be out of date',
  },
  nav: { home: 'Home', history: 'History', readiness: 'Readiness', settings: 'Settings', menu: 'Open menu', closeMenu: 'Close menu', profile: 'Emergency profile', signOut: 'Sign out' },
  status: { online: 'Online', offline: 'Offline', connected: 'Live updates connected', connecting: 'Connecting to live updates', polling: 'Live updates unavailable · checking periodically' },
  landing: { eyebrow: 'Clear next steps when every second matters', title: 'Turn panic into coordinated action.', body: 'Capture what happened, coordinate family, and prepare a clear handover—without delaying a call to emergency services.', start: 'Open demo', nearby: 'Help someone nearby', create: 'Create an account', signIn: 'Sign in', safety: 'Not a doctor, diagnostic system, ambulance provider, or replacement for emergency services.', feature1: 'One calm action at a time', feature2: 'Keep family coordinated', feature3: 'Share only what you approve' },
  auth: { register: 'Create your account', login: 'Welcome back', email: 'Email address', password: 'Password', name: 'Your name', confirmPassword: 'Confirm password', create: 'Create account', signIn: 'Sign in', demo: 'Use fictional demo account', invalid: 'Check your details and try again.', passwordHint: 'Use at least 12 characters with uppercase, lowercase, number, and symbol.', checking: 'Checking your secure session…', privacy: 'Authentication uses secure, HTTP-only cookies. Passwords are never stored in this browser.' },
  home: { greeting: 'Good evening{{name}}', ready: 'Emergency profile is {{score}}% ready', emergency: 'Start emergency help', emergencyHint: 'Press once, then choose who needs help. Calling {{number}} stays available.', active: 'Active emergency', resume: 'Resume coordination', quick: 'Prepare before an emergency', card: 'Emergency card', contacts: 'Contacts', sharing: 'Sharing', simpleTask: 'Current task', familyStatus: 'Family connected' },
  start: { title: 'Who needs help?', subtitle: 'Choose one. You can change this later.', self: 'Help me', family: 'Help a family member', bystander: 'Help someone nearby', category: 'What best describes it?', categoryHint: 'Choose Other if you are not sure.', confirm: 'Continue to describe what happened' },
  category: { chestPain: 'Chest pain', breathingDifficulty: 'Breathing difficulty', fallInjury: 'Fall or injury', unconscious: 'Unconscious person', seizure: 'Seizure', heavyBleeding: 'Heavy bleeding', roadAccident: 'Road accident', allergicReaction: 'Allergic reaction', childEmergency: 'Child emergency', unknown: 'Other or unknown' },
  capture: { title: 'Tell us what happened', subtitle: 'Use your own words. Names and medicine terms stay unchanged.', speak: 'Speak for up to 30 seconds', stop: 'Stop recording', typed: 'Type the situation', placeholder: 'Example: My father suddenly has chest pain and is sweating…', demo: 'Use Hindi demo', submit: 'Use this description', skipAi: 'Skip AI and use selected category', micDenied: 'Microphone access is unavailable. Type what happened below; this will not block help.', location: 'Current location', useLocation: 'Use my location', manualLocation: 'Type location instead', locationDenied: 'Automatic location was not available. Enter a landmark or address.' },
  questions: { title: 'Two quick checks', subtitle: 'Answer only if it is safe to check. You can choose Not sure.', count: 'Question {{current}} of {{total}}', next: 'Next question', finish: 'Show next action', conscious: 'Is the person awake and responding?', breathing: 'Is the person breathing normally?', bleeding: 'Is severe bleeding visible?', fallback: 'This question is shown in English because a reviewed translation is unavailable.' },
  action: { title: 'Do this now', step: 'Action {{current}} of {{total}}', next: 'Next action', coordinate: 'Open coordination room', uncertainty: 'Some details are uncertain', confirmFacts: 'Confirm what we understood', original: 'Original description', language: '{{language}} detected · {{confidence}}% confidence', noClaim: 'Opening the phone dialler does not confirm that a call connected.', callConnected: 'I confirm the call connected', callInitiated: 'Attempting to open your phone dialler. Call connection is not confirmed.', translationFallback: 'A reviewed translation is unavailable, so approved protocol text is shown in English.' },
  room: { title: 'Emergency coordination', patient: 'Emergency for {{name}}', live: 'Live situation', location: 'Location', participants: 'People connected', tasks: 'Family tasks', timeline: 'Timeline', responder: 'Responder brief', handover: 'Hospital handover', share: 'Bystander QR', close: 'Close emergency', closeConfirm: 'Close this emergency session?', closeWarning: 'This stops live coordination. It does not contact or cancel emergency services.', markDeparted: 'Patient departed', markArrived: 'Arrived at hospital' },
  tasks: { title: 'Family task board', assign: 'Assign', accept: 'Accept', decline: 'Decline', complete: 'Mark complete', open: 'Open', accepted: 'Accepted', declined: 'Declined', completed: 'Completed', owner: 'Assigned to {{name}}', updated: 'Updated {{time}}', empty: 'No tasks yet.' },
  brief: { responderTitle: 'Responder brief', responderIntro: 'Only emergency-relevant information approved for sharing.', handoverTitle: 'Hospital handover', handoverIntro: 'Facts are separated by source and confirmation status.', identity: 'Patient identity', observations: 'Reported observations', allergies: 'Critical allergies', conditions: 'Relevant conditions', medicines: 'Current medicines', procedure: 'Relevant procedure', contact: 'Emergency contact', confidence: 'AI interpretation confidence', uncertainty: 'Unconfirmed or unknown', timeline: 'Chronological timeline', original: 'Original description', protocol: 'Protocol version', copy: 'Copy brief', copied: 'Brief copied' },
  bystander: { title: 'Emergency information', limited: 'Limited, time-bound view', call: 'Call {{number}} now', report: 'Report what you can see', conscious: 'Is the person conscious?', breathing: 'Are they breathing normally?', bleeding: 'Is there severe bleeding?', location: 'Share current location', consent: 'Share only after the person consents when possible.', contact: 'Contact family', expired: 'This emergency link is invalid, expired, or revoked.', noRecords: 'This link never shows full medical records, insurance, private documents, or home address.', anonymousIntro: 'No account is needed. Describe only what is necessary for this emergency.', submit: 'Start limited emergency session', privateToken: 'Your temporary access stays in this page and is not added to the link.' },
  profile: { title: 'Emergency profile', intro: 'This information is self-reported unless marked verified.', personal: 'Personal details', dob: 'Date of birth', blood: 'Blood group (optional)', allergies: 'Allergies', conditions: 'Medical conditions', medicines: 'Current medicines', procedures: 'Relevant procedures', hospital: 'Preferred hospital', doctor: 'Doctor contact', insurance: 'Insurance details', reviewed: 'Last reviewed', edit: 'Edit profile' },
  contacts: { title: 'Emergency contacts', intro: 'Add at least two people who can help coordinate.', add: 'Add contact', verified: 'Verified', unverified: 'Not verified', phone: 'Phone number', relationship: 'Relationship' },
  sharing: { title: 'Privacy and sharing', intro: 'Choose the minimum information a bystander may see during an active emergency.', identity: 'Name and approximate age', allergies: 'Critical allergies', conditions: 'Relevant conditions', medicines: 'Current medicines', contact: 'Emergency contact', hidden: 'Insurance, address, and private documents stay hidden by default.', revoke: 'Revoke emergency link' },
  onboarding: { title: 'Prepare your emergency profile', step: 'Step {{current}} of {{total}}', language: 'Language and response', details: 'About the patient', health: 'Self-reported health information', people: 'Emergency contacts', privacy: 'Emergency sharing', review: 'Review and approve', response: 'How should responses appear?', text: 'Text', audio: 'Audio', both: 'Text and audio', approve: 'I confirm this information is accurate to the best of my knowledge', finish: 'Finish setup' },
  readiness: { title: 'Emergency readiness', score: '{{score}} out of 100', deterministic: 'Based only on completed preparation checks—not AI.', complete: 'Ready now', improve: 'Improve next', qr: 'Generate emergency QR' },
  history: { title: 'Emergency history', intro: 'Only sessions you are authorized to view appear here.', active: 'Active', closed: 'Closed', open: 'Open session', empty: 'No emergency sessions yet.' },
  settings: { title: 'Settings', language: 'Language', accessibility: 'Accessibility', appearance: 'Appearance', light: 'Light', dark: 'Dark', system: 'Use device setting', simple: 'Simple mode', simpleHint: 'Shows only the most important emergency controls.', offlineCard: 'Minimal offline emergency card', offlineHint: 'Opt in to save only approved critical fields on this device.', emergencyNumber: 'Emergency number', signOut: 'Sign out' },
  accessibility: { title: 'Accessibility', highContrast: 'High contrast', largeText: 'Larger text', reducedMotion: 'Reduce motion', keyboard: 'All controls support keyboard and switch access.', zoom: 'Layouts remain usable at 200% zoom.' },
  languages: { title: 'Language', current: 'Display language', note: 'English and Hindi are complete. Other listed languages currently include core emergency controls and fall back to English.' },
  offline: { title: 'You are offline', body: 'Calling {{number}} still uses your phone network. AI and live coordination need internet access.', card: 'Saved emergency card', none: 'No offline card is saved on this device.', queue: '{{count}} noncritical update waiting to sync', retry: 'Check connection', protocol: 'Cached reviewed guidance' },
  errors: { genericTitle: 'Something went wrong', genericBody: 'Your emergency-call option is still available. Reload this page or return home.', reload: 'Reload page', home: 'Return home', unauthorizedTitle: 'You cannot view this page', unauthorizedBody: 'Sign in with an account that participates in this emergency.', notFoundTitle: 'Page not found', notFoundBody: 'The link may be outdated or incomplete.' },
};

const hi: typeof en = {
  common: { appName: 'गोल्डन आवर AI', tagline: 'घबराहट को समन्वित कार्रवाई में बदलें।', call: '{{number}} पर कॉल करें', callEmergency: 'आपातकालीन सेवा को कॉल करें', cancel: 'रद्द करें', continue: 'आगे बढ़ें', back: 'पीछे', save: 'बदलाव सहेजें', done: 'हो गया', yes: 'हाँ', no: 'नहीं', unknown: 'पता नहीं', loading: 'लोड हो रहा है…', retry: 'फिर कोशिश करें', demoLabel: 'केवल प्रदर्शन', protocolDisclaimer: 'प्रदर्शन मार्गदर्शन—उत्पादन में उपयोग से पहले चिकित्सकीय समीक्षा आवश्यक है।', selfReported: 'स्वयं बताई गई जानकारी', stale: 'ऑफ़लाइन सहेजा गया · पुराना हो सकता है' },
  nav: { home: 'होम', history: 'इतिहास', readiness: 'तैयारी', settings: 'सेटिंग्स', menu: 'मेन्यू खोलें', closeMenu: 'मेन्यू बंद करें', profile: 'आपातकालीन प्रोफ़ाइल', signOut: 'साइन आउट' },
  status: { online: 'ऑनलाइन', offline: 'ऑफ़लाइन', connected: 'लाइव अपडेट जुड़े हैं', connecting: 'लाइव अपडेट जुड़ रहे हैं', polling: 'लाइव अपडेट उपलब्ध नहीं · समय-समय पर जाँच हो रही है' },
  landing: { eyebrow: 'हर सेकंड महत्वपूर्ण होने पर स्पष्ट अगला कदम', title: 'घबराहट को समन्वित कार्रवाई में बदलें।', body: 'क्या हुआ दर्ज करें, परिवार को जोड़ें और स्पष्ट अस्पताल हैंडओवर तैयार करें—आपातकालीन सेवा को कॉल करने में देरी किए बिना।', start: 'डेमो खोलें', nearby: 'पास के व्यक्ति की मदद करें', create: 'खाता बनाएँ', signIn: 'साइन इन', safety: 'यह डॉक्टर, निदान प्रणाली, एम्बुलेंस प्रदाता या आपातकालीन सेवाओं का विकल्प नहीं है।', feature1: 'एक समय में एक सरल कदम', feature2: 'परिवार को समन्वित रखें', feature3: 'केवल स्वीकृत जानकारी साझा करें' },
  auth: { register: 'अपना खाता बनाएँ', login: 'वापसी पर स्वागत है', email: 'ईमेल पता', password: 'पासवर्ड', name: 'आपका नाम', confirmPassword: 'पासवर्ड की पुष्टि करें', create: 'खाता बनाएँ', signIn: 'साइन इन', demo: 'काल्पनिक डेमो खाता उपयोग करें', invalid: 'जानकारी जाँचकर फिर कोशिश करें।', passwordHint: 'कम से कम 12 अक्षर रखें, जिनमें बड़ा अक्षर, छोटा अक्षर, संख्या और चिह्न हो।', checking: 'आपका सुरक्षित सत्र जाँचा जा रहा है…', privacy: 'प्रमाणीकरण सुरक्षित HTTP-only कुकी का उपयोग करता है। पासवर्ड इस ब्राउज़र में कभी सहेजे नहीं जाते।' },
  home: { greeting: 'शुभ संध्या{{name}}', ready: 'आपातकालीन प्रोफ़ाइल {{score}}% तैयार है', emergency: 'आपातकालीन मदद शुरू करें', emergencyHint: 'एक बार दबाएँ, फिर चुनें किसे मदद चाहिए। {{number}} पर कॉल हमेशा उपलब्ध है।', active: 'सक्रिय आपातकाल', resume: 'समन्वय जारी रखें', quick: 'आपातकाल से पहले तैयारी', card: 'आपातकालीन कार्ड', contacts: 'संपर्क', sharing: 'साझाकरण', simpleTask: 'वर्तमान कार्य', familyStatus: 'परिवार जुड़ा है' },
  start: { title: 'किसे मदद चाहिए?', subtitle: 'एक चुनें। इसे बाद में बदला जा सकता है।', self: 'मेरी मदद करें', family: 'परिवार के सदस्य की मदद करें', bystander: 'पास के व्यक्ति की मदद करें', category: 'स्थिति का सबसे अच्छा वर्णन क्या है?', categoryHint: 'पता न हो तो अन्य चुनें।', confirm: 'क्या हुआ बताने के लिए आगे बढ़ें' },
  category: { chestPain: 'सीने में दर्द', breathingDifficulty: 'साँस लेने में कठिनाई', fallInjury: 'गिरना या चोट', unconscious: 'बेहोश व्यक्ति', seizure: 'दौरा', heavyBleeding: 'बहुत रक्तस्राव', roadAccident: 'सड़क दुर्घटना', allergicReaction: 'एलर्जी प्रतिक्रिया', childEmergency: 'बच्चे की आपात स्थिति', unknown: 'अन्य या अज्ञात' },
  capture: { title: 'बताएँ क्या हुआ', subtitle: 'अपने शब्दों में बताएँ। नाम और दवा के नाम नहीं बदले जाएँगे।', speak: 'अधिकतम 30 सेकंड बोलें', stop: 'रिकॉर्डिंग रोकें', typed: 'स्थिति लिखें', placeholder: 'उदाहरण: मेरे पिताजी को अचानक सीने में दर्द और पसीना आ रहा है…', demo: 'हिंदी डेमो उपयोग करें', submit: 'इस विवरण का उपयोग करें', skipAi: 'AI छोड़ें और चुनी श्रेणी उपयोग करें', micDenied: 'माइक्रोफ़ोन उपलब्ध नहीं है। नीचे लिखें; इससे मदद नहीं रुकेगी।', location: 'वर्तमान स्थान', useLocation: 'मेरा स्थान उपयोग करें', manualLocation: 'स्थान लिखें', locationDenied: 'स्वचालित स्थान उपलब्ध नहीं था। कोई लैंडमार्क या पता लिखें।' },
  questions: { title: 'दो छोटे सवाल', subtitle: 'सुरक्षित हो तभी जाँचें। पता नहीं चुन सकते हैं।', count: 'सवाल {{current}} / {{total}}', next: 'अगला सवाल', finish: 'अगला कदम दिखाएँ', conscious: 'क्या व्यक्ति जाग रहा है और प्रतिक्रिया दे रहा है?', breathing: 'क्या व्यक्ति सामान्य रूप से साँस ले रहा है?', bleeding: 'क्या बहुत रक्तस्राव दिखाई दे रहा है?', fallback: 'समीक्षित अनुवाद उपलब्ध नहीं है, इसलिए यह सवाल अंग्रेज़ी में दिख रहा है।' },
  action: { title: 'अभी यह करें', step: 'कदम {{current}} / {{total}}', next: 'अगला कदम', coordinate: 'समन्वय कक्ष खोलें', uncertainty: 'कुछ जानकारी अनिश्चित है', confirmFacts: 'समझी गई बातों की पुष्टि करें', original: 'मूल विवरण', language: '{{language}} पहचानी गई · {{confidence}}% विश्वास', noClaim: 'फ़ोन डायलर खुलना कॉल जुड़ने की पुष्टि नहीं है।', callConnected: 'मैं पुष्टि करता/करती हूँ कि कॉल जुड़ गई', callInitiated: 'फ़ोन डायलर खोलने की कोशिश हो रही है। कॉल जुड़ने की पुष्टि नहीं है।', translationFallback: 'समीक्षित अनुवाद उपलब्ध नहीं है, इसलिए स्वीकृत प्रोटोकॉल अंग्रेज़ी में दिख रहा है।' },
  room: { title: 'आपातकालीन समन्वय', patient: '{{name}} के लिए आपात स्थिति', live: 'वर्तमान स्थिति', location: 'स्थान', participants: 'जुड़े हुए लोग', tasks: 'परिवार के कार्य', timeline: 'समयरेखा', responder: 'रिस्पॉन्डर विवरण', handover: 'अस्पताल हैंडओवर', share: 'राहगीर QR', close: 'आपात स्थिति बंद करें', closeConfirm: 'यह आपात सत्र बंद करें?', closeWarning: 'इससे लाइव समन्वय रुकता है। यह आपातकालीन सेवा को संपर्क या रद्द नहीं करता।', markDeparted: 'मरीज़ रवाना हुआ', markArrived: 'अस्पताल पहुँचा' },
  tasks: { title: 'परिवार कार्य बोर्ड', assign: 'सौंपें', accept: 'स्वीकार करें', decline: 'मना करें', complete: 'पूरा चिह्नित करें', open: 'खुला', accepted: 'स्वीकृत', declined: 'अस्वीकृत', completed: 'पूरा', owner: '{{name}} को सौंपा', updated: '{{time}} पर अपडेट', empty: 'अभी कोई कार्य नहीं।' },
  brief: { responderTitle: 'रिस्पॉन्डर विवरण', responderIntro: 'केवल साझा करने के लिए स्वीकृत आपात जानकारी।', handoverTitle: 'अस्पताल हैंडओवर', handoverIntro: 'जानकारी स्रोत और पुष्टि स्थिति के अनुसार अलग है।', identity: 'मरीज़ की पहचान', observations: 'बताई गई बातें', allergies: 'महत्वपूर्ण एलर्जी', conditions: 'संबंधित स्थितियाँ', medicines: 'वर्तमान दवाएँ', procedure: 'संबंधित प्रक्रिया', contact: 'आपात संपर्क', confidence: 'AI समझ का विश्वास', uncertainty: 'अपुष्ट या अज्ञात', timeline: 'क्रमानुसार समयरेखा', original: 'मूल विवरण', protocol: 'प्रोटोकॉल संस्करण', copy: 'विवरण कॉपी करें', copied: 'विवरण कॉपी हुआ' },
  bystander: { title: 'आपात जानकारी', limited: 'सीमित, समयबद्ध दृश्य', call: 'अभी {{number}} पर कॉल करें', report: 'जो देख सकते हैं वह बताएँ', conscious: 'क्या व्यक्ति होश में है?', breathing: 'क्या सामान्य साँस ले रहा है?', bleeding: 'क्या बहुत रक्तस्राव है?', location: 'वर्तमान स्थान साझा करें', consent: 'संभव हो तो व्यक्ति की सहमति के बाद ही साझा करें।', contact: 'परिवार से संपर्क करें', expired: 'यह आपात लिंक अमान्य, समाप्त या रद्द है।', noRecords: 'इस लिंक पर पूर्ण मेडिकल रिकॉर्ड, बीमा, निजी दस्तावेज़ या घर का पता कभी नहीं दिखता।', anonymousIntro: 'खाते की जरूरत नहीं है। केवल इस आपात स्थिति के लिए जरूरी जानकारी बताएँ।', submit: 'सीमित आपात सत्र शुरू करें', privateToken: 'आपकी अस्थायी पहुँच इसी पेज में रहती है और लिंक में नहीं जोड़ी जाती।' },
  profile: { title: 'आपातकालीन प्रोफ़ाइल', intro: 'सत्यापित चिह्न न हो तो यह जानकारी स्वयं बताई गई है।', personal: 'व्यक्तिगत जानकारी', dob: 'जन्म तिथि', blood: 'रक्त समूह (वैकल्पिक)', allergies: 'एलर्जी', conditions: 'चिकित्सकीय स्थितियाँ', medicines: 'वर्तमान दवाएँ', procedures: 'संबंधित प्रक्रियाएँ', hospital: 'पसंदीदा अस्पताल', doctor: 'डॉक्टर संपर्क', insurance: 'बीमा जानकारी', reviewed: 'अंतिम समीक्षा', edit: 'प्रोफ़ाइल बदलें' },
  contacts: { title: 'आपात संपर्क', intro: 'समन्वय में मदद कर सकने वाले कम से कम दो लोग जोड़ें।', add: 'संपर्क जोड़ें', verified: 'सत्यापित', unverified: 'सत्यापित नहीं', phone: 'फ़ोन नंबर', relationship: 'संबंध' },
  sharing: { title: 'गोपनीयता और साझाकरण', intro: 'सक्रिय आपात स्थिति में राहगीर को दिखने वाली न्यूनतम जानकारी चुनें।', identity: 'नाम और अनुमानित आयु', allergies: 'महत्वपूर्ण एलर्जी', conditions: 'संबंधित स्थितियाँ', medicines: 'वर्तमान दवाएँ', contact: 'आपात संपर्क', hidden: 'बीमा, पता और निजी दस्तावेज़ डिफ़ॉल्ट रूप से छिपे रहते हैं।', revoke: 'आपात लिंक रद्द करें' },
  onboarding: { title: 'आपातकालीन प्रोफ़ाइल तैयार करें', step: 'कदम {{current}} / {{total}}', language: 'भाषा और उत्तर', details: 'मरीज़ के बारे में', health: 'स्वयं बताई स्वास्थ्य जानकारी', people: 'आपात संपर्क', privacy: 'आपात साझाकरण', review: 'समीक्षा और स्वीकृति', response: 'उत्तर कैसे दिखें?', text: 'टेक्स्ट', audio: 'ऑडियो', both: 'टेक्स्ट और ऑडियो', approve: 'मेरी जानकारी के अनुसार यह सही है', finish: 'सेटअप पूरा करें' },
  readiness: { title: 'आपात तैयारी', score: '100 में {{score}}', deterministic: 'केवल पूरी की गई तैयारी जाँच पर आधारित—AI पर नहीं।', complete: 'अभी तैयार', improve: 'अगला सुधार', qr: 'आपात QR बनाएँ' },
  history: { title: 'आपात इतिहास', intro: 'केवल अधिकृत सत्र यहाँ दिखाई देते हैं।', active: 'सक्रिय', closed: 'बंद', open: 'सत्र खोलें', empty: 'अभी कोई आपात सत्र नहीं।' },
  settings: { title: 'सेटिंग्स', language: 'भाषा', accessibility: 'सुगम्यता', appearance: 'रूप', light: 'हल्का', dark: 'गहरा', system: 'डिवाइस सेटिंग', simple: 'सरल मोड', simpleHint: 'केवल सबसे महत्वपूर्ण आपात नियंत्रण दिखाता है।', offlineCard: 'न्यूनतम ऑफ़लाइन आपात कार्ड', offlineHint: 'इस डिवाइस पर केवल स्वीकृत महत्वपूर्ण जानकारी सहेजें।', emergencyNumber: 'आपात नंबर', signOut: 'साइन आउट' },
  accessibility: { title: 'सुगम्यता', highContrast: 'अधिक कंट्रास्ट', largeText: 'बड़ा टेक्स्ट', reducedMotion: 'कम गति', keyboard: 'सभी नियंत्रण कीबोर्ड और स्विच से चलते हैं।', zoom: 'लेआउट 200% ज़ूम पर भी उपयोगी है।' },
  languages: { title: 'भाषा', current: 'दिखाने की भाषा', note: 'अंग्रेज़ी और हिंदी पूर्ण हैं। अन्य भाषाओं में मुख्य आपात नियंत्रण हैं और बाकी अंग्रेज़ी में दिखते हैं।' },
  offline: { title: 'आप ऑफ़लाइन हैं', body: '{{number}} पर कॉल फ़ोन नेटवर्क से होती है। AI और लाइव समन्वय के लिए इंटरनेट चाहिए।', card: 'सहेजा आपात कार्ड', none: 'इस डिवाइस पर ऑफ़लाइन कार्ड नहीं है।', queue: '{{count}} गैर-महत्वपूर्ण अपडेट सिंक होने बाकी', retry: 'कनेक्शन जाँचें', protocol: 'कैश किया समीक्षा-युक्त मार्गदर्शन' },
  errors: { genericTitle: 'कुछ गड़बड़ हुई', genericBody: 'आपात कॉल विकल्प अभी भी उपलब्ध है। पेज फिर लोड करें या होम जाएँ।', reload: 'पेज फिर लोड करें', home: 'होम जाएँ', unauthorizedTitle: 'आप यह पेज नहीं देख सकते', unauthorizedBody: 'इस आपात स्थिति में शामिल खाते से साइन इन करें।', notFoundTitle: 'पेज नहीं मिला', notFoundBody: 'लिंक पुराना या अधूरा हो सकता है।' },
};

const emergencyResources = {
  mr: { title: 'आपत्कालीन मदत', start: 'आपत्कालीन मदत सुरू करा', call: '{{number}} वर कॉल करा', yes: 'होय', no: 'नाही' },
  ta: { title: 'அவசர உதவி', start: 'அவசர உதவியைத் தொடங்கு', call: '{{number}} ஐ அழைக்கவும்', yes: 'ஆம்', no: 'இல்லை' },
  te: { title: 'అత్యవసర సహాయం', start: 'అత్యవసర సహాయం ప్రారంభించండి', call: '{{number}}కు కాల్ చేయండి', yes: 'అవును', no: 'కాదు' },
  kn: { title: 'ತುರ್ತು ಸಹಾಯ', start: 'ತುರ್ತು ಸಹಾಯ ಪ್ರಾರಂಭಿಸಿ', call: '{{number}} ಗೆ ಕರೆ ಮಾಡಿ', yes: 'ಹೌದು', no: 'ಇಲ್ಲ' },
  bn: { title: 'জরুরি সহায়তা', start: 'জরুরি সহায়তা শুরু করুন', call: '{{number}} নম্বরে কল করুন', yes: 'হ্যাঁ', no: 'না' },
  gu: { title: 'કટોકટી સહાય', start: 'કટોકટી સહાય શરૂ કરો', call: '{{number}} પર કૉલ કરો', yes: 'હા', no: 'ના' },
  pa: { title: 'ਐਮਰਜੈਂਸੀ ਮਦਦ', start: 'ਐਮਰਜੈਂਸੀ ਮਦਦ ਸ਼ੁਰੂ ਕਰੋ', call: '{{number}} ਉੱਤੇ ਕਾਲ ਕਰੋ', yes: 'ਹਾਂ', no: 'ਨਹੀਂ' },
  ur: { title: 'ہنگامی مدد', start: 'ہنگامی مدد شروع کریں', call: '{{number}} پر کال کریں', yes: 'ہاں', no: 'نہیں' },
};

const resources: Record<string, { translation: typeof en | Record<string, unknown> }> = {
  en: { translation: en },
  hi: { translation: hi },
};

for (const [code, emergency] of Object.entries(emergencyResources)) {
  resources[code] = {
    translation: {
      common: { call: emergency.call, yes: emergency.yes, no: emergency.no },
      home: { emergency: emergency.start },
      start: { title: emergency.title },
    },
  };
}

export const supportedLanguages = [
  { code: 'en', label: 'English' },
  { code: 'hi', label: 'हिन्दी' },
  { code: 'mr', label: 'मराठी' },
  { code: 'ta', label: 'தமிழ்' },
  { code: 'te', label: 'తెలుగు' },
  { code: 'kn', label: 'ಕನ್ನಡ' },
  { code: 'bn', label: 'বাংলা' },
  { code: 'gu', label: 'ગુજરાતી' },
  { code: 'pa', label: 'ਪੰਜਾਬੀ' },
  { code: 'ur', label: 'اردو' },
] as const;

const storedLanguage = typeof window === 'undefined' ? 'en' : window.localStorage.getItem('gh-language') ?? 'en';

void i18n.use(initReactI18next).init({
  resources,
  lng: storedLanguage,
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnNull: false,
});

export function applyDocumentLanguage(language: string): void {
  if (typeof document === 'undefined') return;
  document.documentElement.lang = language;
  document.documentElement.dir = language === 'ur' ? 'rtl' : 'ltr';
}

applyDocumentLanguage(storedLanguage);

export default i18n;
