# Hesper in the Microsoft Store — what to enter in Partner Center

Everything a submission asks for, in the order Partner Center shows it. The texts are ready to
paste: English first, then French, Spanish, Portuguese (Brazil), Japanese and Chinese (Simplified),
the app's six languages (the package declares them, so the Store offers a listing in each).

Files, made on the Windows PC:
- the package: `.\scripts\store_windows.ps1` → `build-cache\store\Hesper-<version>.msix`
- the pictures: `.\scripts\store_screenshots.ps1` (and `-Lang fr`…) → `build-cache\store-shots\<lang>\`

## 0. Once: the app's name and identity

1. Partner Center → Apps and games → **New product** → **MSIX or PWA app** → reserve **Hesper**.
2. Hesper → Product management → **Product identity**: copy `Package/Identity/Name`,
   `Package/Identity/Publisher` and `Package/Properties/PublisherDisplayName` into
   `win/Store/identity.json` (they're public: every installed package carries them), commit, then
   build the package.

## 1. Pricing and availability

- **Markets:** all.
- **Discoverability:** Public — make it available and discoverable in the Store.
- **Schedule:** as soon as it passes certification (or "manually" to choose the day).
- **Pricing:** Free. No free trial, no sale.

## 2. Properties

- **Category:** Productivity.
- **Privacy policy URL:** https://github.com/Gbally/Hesper/blob/main/PRIVACY.md
- **Website:** https://gbally.github.io/Hesper/
- **Support contact info:** https://github.com/Gbally/Hesper/issues
- **Personal information:** Yes, it accesses personal information (it records meetings; everything
  stays on the computer, as the privacy policy says).
- **Product declarations:**
  - "Windows can include this app's data in automatic backups to OneDrive": **untick** (meetings
    stay on the PC unless the user picks a synced notes folder).
  - "Customers can install this app to alternate drives or removable storage": leave ticked.
  - Accessibility, pen, Xbox, non-Microsoft drivers: not ticked.
- **System requirements:**
  - Minimum: Microphone; Memory 8 GB.
  - Recommended: Memory 16 GB.
  - (Graphics: nothing required. An NVIDIA card makes the notes faster; the description says so.)

## 3. Age ratings (IARC questionnaire)

- Category: **Productivity, utility or other (not a game)**.
- Violence, fear, sexual content, language, drugs, alcohol, tobacco, gambling: **No** to all.
- Users can interact or exchange content with each other: **No** (the notes are the user's own;
  e-mail drafts open in the user's own mail app).
- Shares the user's location with others: **No**.
- Digital purchases: **No**.
- Unrestricted internet access (a web browser inside the app): **No**.

Expected rating: 3+ / Everyone.

## 4. Packages

- Upload `Hesper-<version>.msix` (from `store_windows.ps1`, not `-Test`).
- Device families: **Windows 10/11 Desktop** only.
- Each new upload needs a higher version (0.7.3.0, then 0.7.4.0…).

## 5. Submission options

**Restricted capabilities** (`runFullTrust`), why it is needed:

> Hesper is a .NET desktop app (WinForms and WebView2) that lives in the notification area. It needs
> full trust to record the microphone and the sound the PC plays (WASAPI loopback, so the other
> people in a call are recorded too), to run its local AI engine (Python and llama.cpp processes,
> installed on first launch), and, if the user turns it on, to read classic Outlook's calendar
> through COM. Nothing is sent to any server.

**Notes for certification:**

> Hesper records meetings and writes notes with AI that runs on the PC. No account or sign-in.
>
> First launch: the welcome window sets up the AI engine. It downloads, once, uv and Python packages
> (PyPI, GitHub), llama.cpp (GitHub) and the AI models (Hugging Face): about 4–6 GB, 5–15 minutes
> depending on the connection (uv, llama.cpp and the Python packages are pinned and checked by
> hash). This is described in the listing. After that Hesper works offline.
>
> To test: 1. finish the welcome window. 2. Click Hesper's icon in the notification area (maybe
> under ^), then the record button; speak or play a video for a minute. 3. Click "Stop & create
> notes": the notes open after a few minutes (faster with an NVIDIA graphics card). 16 GB of
> memory recommended.
>
> Source code (MIT): https://github.com/Gbally/Hesper

## 6. Store listing — the same in every language

- **Product name:** Hesper
- **Screenshots (Desktop):** `01-notes.png`, `02-recording.png`, `03-transcript.png`, `04-dark.png`
  from `build-cache\store-shots\<lang>\` (the app in that language; `en` if not made), each with its
  caption below.
- **Store logos → 1:1 app tile icon (300 × 300):** `tile-300x300.png`.
- **Super hero art (16:9, optional):** `hero-1920x1080.png`.
- **Copyright and trademark info:** © 2026 Gbally
- **Additional license terms:** Free and open source software under the MIT License:
  https://github.com/Gbally/Hesper/blob/main/LICENSE
- **Developed by:** Gbally
- **What's new in this version:** leave empty for the first submission.

---

### English (en-US)

**Description**

```
Hesper records your meetings and turns them into notes, on your PC: nothing is sent to the cloud.

It records your microphone and what your PC plays (Teams, Zoom, Google Meet, Webex…), works out who said what, and writes a summary, the decisions, the open questions and a to-do list with owners and real due dates ("next Friday" becomes a date). Speech recognition (Whisper), speaker separation and the notes (Qwen) all run on your computer, on its NVIDIA graphics card if it has one.

• No account, no subscription, no cloud: recordings, transcripts and notes are plain files in a folder you choose.
• One click in the notification area, a keyboard shortcut (Ctrl+Alt+R), or automatically when a call starts.
• To-dos are checked against what was really said, so nothing is made up.
• Name a colleague once and Hesper recognises their voice in later meetings.
• Ask a question across your meetings and get an answer with its sources.
• Classic Outlook's calendar (or a calendar link) names the meeting and its attendees.
• The app speaks English, French, Spanish, Portuguese, Japanese and Chinese; notes are written in the meeting's language.

On first launch, Hesper downloads its AI engine and models (about 4–6 GB, once); after that it works offline. Windows 10 or 11, 16 GB of memory recommended, about 8 GB of free disk space.

Free and open source (MIT): https://github.com/Gbally/Hesper

Always tell the other participants that you're recording, and follow your company's rules and the law where you are.
```

**Short description**

```
Private meeting notes, fully offline: Hesper records your calls and writes the summary, decisions and to-dos with AI that runs on your PC.
```

**Product features**

```
Records your microphone and your PC's sound: Teams, Zoom, Google Meet, Webex and more
Summary, decisions, open questions and to-dos with owners and due dates
Runs entirely on your PC: no cloud, no account, no subscription
Faster with an NVIDIA graphics card
Knows who spoke, and remembers the voices you name
Ask questions across all your meetings, with sources
Every to-do from every meeting in one list
Meeting types: general, 1:1, stand-up, client call, interview, workshop
Classic Outlook's calendar or a calendar link names meetings and attendees
E-mail the notes as a draft in your mail app, Outlook or Gmail
Plain files in a folder you choose; audio deleted after 7 days by default
Light and dark, in six languages
Free and open source (MIT)
```

**Screenshot captions**

1. Notes after each meeting: summary, decisions and to-dos with owners and due dates.
2. Record from the notification area, and see every to-do that's yours.
3. A transcript that knows who spoke, and answers drawn from your meetings.
4. Light or dark, like Windows.

**Search terms** (7)

```
meeting notes
transcription
meeting recorder
offline AI
speech to text
meeting minutes
action items
```

---

### Français (fr-FR)

**Description**

```
Hesper enregistre vos réunions et en fait des notes, sur votre PC : rien n'est envoyé dans le cloud.

Il enregistre votre micro et ce que joue votre PC (Teams, Zoom, Google Meet, Webex…), retrouve qui a dit quoi, et rédige un résumé, les décisions, les questions ouvertes et une liste de tâches avec responsables et vraies échéances (« vendredi prochain » devient une date). La reconnaissance vocale (Whisper), la séparation des voix et les notes (Qwen) tournent entièrement sur votre ordinateur, sur sa carte graphique NVIDIA s'il en a une.

• Ni compte, ni abonnement, ni cloud : enregistrements, transcriptions et notes sont de simples fichiers dans le dossier de votre choix.
• Un clic dans la zone de notification, un raccourci clavier (Ctrl+Alt+R), ou automatiquement quand un appel commence.
• Les tâches sont vérifiées par rapport à ce qui a vraiment été dit : rien n'est inventé.
• Nommez un collègue une fois, Hesper reconnaît sa voix aux réunions suivantes.
• Posez une question sur l'ensemble de vos réunions et obtenez une réponse avec ses sources.
• L'agenda d'Outlook classique (ou un lien d'agenda) nomme la réunion et ses participants.
• L'app parle français, anglais, espagnol, portugais, japonais et chinois ; les notes sont rédigées dans la langue de la réunion.

Au premier lancement, Hesper télécharge son moteur et ses modèles d'IA (environ 4 à 6 Go, une seule fois) ; ensuite il fonctionne hors ligne. Windows 10 ou 11, 16 Go de mémoire conseillés, environ 8 Go d'espace disque libre.

Gratuit et open source (MIT) : https://github.com/Gbally/Hesper

Prévenez toujours les autres participants que vous enregistrez, et respectez les règles de votre entreprise et la loi de votre pays.
```

**Short description**

```
Des notes de réunion privées, entièrement hors ligne : Hesper enregistre vos appels et rédige le résumé, les décisions et les tâches avec une IA qui tourne sur votre PC.
```

**Product features**

```
Enregistre votre micro et le son de votre PC : Teams, Zoom, Google Meet, Webex et les autres
Résumé, décisions, questions ouvertes et tâches avec responsables et échéances
Tourne entièrement sur votre PC : ni cloud, ni compte, ni abonnement
Plus rapide avec une carte graphique NVIDIA
Sait qui a parlé, et retient les voix que vous nommez
Posez des questions sur toutes vos réunions, avec les sources
Toutes les tâches de toutes vos réunions dans une seule liste
Types de réunion : général, 1:1, point d'équipe, appel client, entretien, atelier
L'agenda d'Outlook classique ou un lien d'agenda nomme les réunions et les participants
Envoyez les notes en brouillon dans votre messagerie, Outlook ou Gmail
De simples fichiers dans le dossier de votre choix ; l'audio est supprimé après 7 jours par défaut
Clair et sombre, en six langues
Gratuit et open source (MIT)
```

**Screenshot captions**

1. Des notes après chaque réunion : résumé, décisions et tâches avec responsables et échéances.
2. Enregistrez depuis la zone de notification, et retrouvez toutes les tâches qui sont les vôtres.
3. Une transcription qui sait qui a parlé, et des réponses tirées de vos réunions.
4. Clair ou sombre, comme Windows.

**Search terms**

```
notes de réunion
transcription
compte rendu
IA hors ligne
reconnaissance vocale
enregistreur de réunion
liste de tâches
```

---

### Español (es-ES)

**Description**

```
Hesper graba tus reuniones y las convierte en notas, en tu PC: nada se envía a la nube.

Graba tu micrófono y lo que reproduce tu PC (Teams, Zoom, Google Meet, Webex…), averigua quién dijo qué y redacta un resumen, las decisiones, las preguntas abiertas y una lista de tareas con responsables y fechas reales ("el viernes que viene" se convierte en una fecha). El reconocimiento de voz (Whisper), la separación de voces y las notas (Qwen) funcionan por completo en tu ordenador, en su tarjeta gráfica NVIDIA si la tiene.

• Sin cuenta, sin suscripción, sin nube: grabaciones, transcripciones y notas son archivos normales en la carpeta que elijas.
• Un clic en el área de notificación, un atajo de teclado (Ctrl+Alt+R) o automáticamente cuando empieza una llamada.
• Las tareas se comprueban con lo que realmente se dijo: no se inventa nada.
• Pon nombre a un compañero una vez y Hesper reconocerá su voz en las siguientes reuniones.
• Haz una pregunta sobre todas tus reuniones y obtén una respuesta con sus fuentes.
• El calendario de Outlook clásico (o un enlace de calendario) pone nombre a la reunión y a sus asistentes.
• La app está en español, inglés, francés, portugués, japonés y chino; las notas se escriben en el idioma de la reunión.

La primera vez, Hesper descarga su motor y sus modelos de IA (unos 4–6 GB, una sola vez); después funciona sin conexión. Windows 10 u 11, 16 GB de memoria recomendados, unos 8 GB de espacio libre en disco.

Gratis y de código abierto (MIT): https://github.com/Gbally/Hesper

Avisa siempre a los demás participantes de que estás grabando, y respeta las normas de tu empresa y la ley de tu país.
```

**Short description**

```
Notas de reunión privadas y sin conexión: Hesper graba tus llamadas y redacta el resumen, las decisiones y las tareas con una IA que funciona en tu PC.
```

**Product features**

```
Graba tu micrófono y el sonido de tu PC: Teams, Zoom, Google Meet, Webex y más
Resumen, decisiones, preguntas abiertas y tareas con responsables y fechas
Funciona por completo en tu PC: sin nube, sin cuenta, sin suscripción
Más rápido con una tarjeta gráfica NVIDIA
Sabe quién habló y recuerda las voces a las que pones nombre
Haz preguntas sobre todas tus reuniones, con las fuentes
Todas las tareas de todas tus reuniones en una sola lista
Tipos de reunión: general, 1:1, stand-up, llamada con cliente, entrevista, taller
El calendario de Outlook clásico o un enlace de calendario nombra reuniones y asistentes
Envía las notas como borrador en tu app de correo, Outlook o Gmail
Archivos normales en la carpeta que elijas; el audio se borra a los 7 días por defecto
Claro y oscuro, en seis idiomas
Gratis y de código abierto (MIT)
```

**Screenshot captions**

1. Notas después de cada reunión: resumen, decisiones y tareas con responsables y fechas.
2. Graba desde el área de notificación y ve todas las tareas que te tocan.
3. Una transcripción que sabe quién habló, y respuestas sacadas de tus reuniones.
4. Claro u oscuro, como Windows.

**Search terms**

```
notas de reunión
transcripción
grabar reuniones
IA sin conexión
voz a texto
acta de reunión
lista de tareas
```

---

### Português (pt-BR)

**Description**

```
O Hesper grava suas reuniões e as transforma em notas, no seu PC: nada é enviado para a nuvem.

Ele grava seu microfone e o que o seu PC reproduz (Teams, Zoom, Google Meet, Webex…), descobre quem disse o quê e escreve um resumo, as decisões, as perguntas em aberto e uma lista de tarefas com responsáveis e prazos reais ("na próxima sexta" vira uma data). O reconhecimento de fala (Whisper), a separação de vozes e as notas (Qwen) rodam inteiramente no seu computador, na placa de vídeo NVIDIA, se houver uma.

• Sem conta, sem assinatura, sem nuvem: gravações, transcrições e notas são arquivos comuns na pasta que você escolher.
• Um clique na área de notificação, um atalho de teclado (Ctrl+Alt+R) ou automaticamente quando uma chamada começa.
• As tarefas são conferidas com o que foi realmente dito: nada é inventado.
• Dê nome a um colega uma vez e o Hesper reconhece a voz dele nas próximas reuniões.
• Faça uma pergunta sobre todas as suas reuniões e receba uma resposta com as fontes.
• O calendário do Outlook clássico (ou um link de calendário) dá nome à reunião e aos participantes.
• O app fala português, inglês, francês, espanhol, japonês e chinês; as notas são escritas no idioma da reunião.

Na primeira vez, o Hesper baixa seu mecanismo e seus modelos de IA (cerca de 4–6 GB, uma única vez); depois funciona offline. Windows 10 ou 11, 16 GB de memória recomendados, cerca de 8 GB de espaço livre em disco.

Gratuito e de código aberto (MIT): https://github.com/Gbally/Hesper

Sempre avise os outros participantes de que você está gravando, e siga as regras da sua empresa e a lei do seu país.
```

**Short description**

```
Notas de reunião privadas e totalmente offline: o Hesper grava suas chamadas e escreve o resumo, as decisões e as tarefas com uma IA que roda no seu PC.
```

**Product features**

```
Grava seu microfone e o som do seu PC: Teams, Zoom, Google Meet, Webex e outros
Resumo, decisões, perguntas em aberto e tarefas com responsáveis e prazos
Roda inteiramente no seu PC: sem nuvem, sem conta, sem assinatura
Mais rápido com uma placa de vídeo NVIDIA
Sabe quem falou e lembra as vozes que você nomeia
Faça perguntas sobre todas as suas reuniões, com as fontes
Todas as tarefas de todas as reuniões em uma só lista
Tipos de reunião: geral, 1:1, daily, chamada com cliente, entrevista, workshop
O calendário do Outlook clássico ou um link de calendário nomeia reuniões e participantes
Envie as notas como rascunho no seu app de e-mail, Outlook ou Gmail
Arquivos comuns na pasta que você escolher; o áudio é apagado após 7 dias por padrão
Claro e escuro, em seis idiomas
Gratuito e de código aberto (MIT)
```

**Screenshot captions**

1. Notas depois de cada reunião: resumo, decisões e tarefas com responsáveis e prazos.
2. Grave pela área de notificação e veja todas as tarefas que são suas.
3. Uma transcrição que sabe quem falou, e respostas tiradas das suas reuniões.
4. Claro ou escuro, como o Windows.

**Search terms**

```
notas de reunião
transcrição
gravar reuniões
IA offline
voz para texto
ata de reunião
lista de tarefas
```

---

### 日本語 (ja-JP)

**Description**

```
Hesper は会議を録音し、PC 上でメモにまとめます。クラウドには何も送信しません。

マイクと PC で再生される音声（Teams、Zoom、Google Meet、Webex など）を録音し、誰が何を話したかを聞き分けて、要約、決定事項、未解決の質問、担当者と実際の期限付きの ToDo リストを作成します（「来週の金曜」は日付になります）。音声認識（Whisper）、話者の分離、メモ作成（Qwen）はすべてお使いのコンピューター上で動作し、NVIDIA のグラフィックス カードがあればそれを使います。

• アカウント不要、サブスクリプション不要、クラウド不要：録音、文字起こし、メモは、選んだフォルダーに普通のファイルとして保存されます。
• 通知領域からワンクリック、キーボード ショートカット（Ctrl+Alt+R）、または通話の開始時に自動で録音。
• ToDo は実際の発言と照合されるので、作り話は入りません。
• 同僚の名前を一度付ければ、次の会議からその声を聞き分けます。
• すべての会議に対して質問し、出典付きの回答を得られます。
• 従来の Outlook の予定表（または予定表のリンク）から、会議名と参加者を取得します。
• アプリは日本語、英語、フランス語、スペイン語、ポルトガル語、中国語に対応。メモは会議の言語で書かれます。

初回起動時に AI エンジンとモデル（約 4～6 GB、一度だけ）をダウンロードします。その後はオフラインで動作します。Windows 10 または 11、メモリ 16 GB 推奨、空きディスク容量 約 8 GB。

無料のオープンソース（MIT）：https://github.com/Gbally/Hesper

録音するときは必ずほかの参加者に伝え、会社の規則と各国の法律に従ってください。
```

**Short description**

```
完全オフラインのプライベートな会議メモ。Hesper は通話を録音し、PC 上で動く AI が要約、決定事項、ToDo をまとめます。
```

**Product features**

```
マイクと PC の音声を録音：Teams、Zoom、Google Meet、Webex など
要約、決定事項、未解決の質問、担当者と期限付きの ToDo
すべて PC 上で動作：クラウド、アカウント、サブスクリプション不要
NVIDIA のグラフィックス カードでさらに高速
誰が話したかを聞き分け、名前を付けた声を覚えます
すべての会議に質問でき、出典も表示
すべての会議の ToDo をひとつのリストに
会議の種類：一般、1:1、朝会、顧客との打ち合わせ、面接、ワークショップ
従来の Outlook の予定表または予定表のリンクから会議名と参加者を取得
メモをメール アプリ、Outlook、Gmail の下書きとして送信
選んだフォルダーに普通のファイルで保存。音声は既定で 7 日後に削除
ライトとダーク、6 か国語に対応
無料のオープンソース（MIT）
```

**Screenshot captions**

1. 会議のたびにメモ：要約、決定事項、担当者と期限付きの ToDo。
2. 通知領域から録音。自分の ToDo もすべて確認できます。
3. 誰が話したかがわかる文字起こしと、会議の内容に基づく回答。
4. Windows に合わせてライトにもダークにも。

**Search terms**

```
会議メモ
文字起こし
議事録
会議 録音
オフライン AI
音声 テキスト変換
ToDo リスト
```

---

### 简体中文 (zh-CN)

**Description**

```
Hesper 录制你的会议并在你的电脑上整理成笔记：任何内容都不会发送到云端。

它会录制你的麦克风以及电脑播放的声音（Teams、Zoom、Google Meet、Webex 等），分辨谁说了什么，并写出摘要、决定、待解决的问题，以及带负责人和真实截止日期的待办清单（“下周五”会变成具体日期）。语音识别（Whisper）、说话人分离和笔记（Qwen）全部在你的电脑上运行；如果有 NVIDIA 显卡，会用显卡加速。

• 无需账户、无需订阅、无需云端：录音、转写和笔记都是你所选文件夹中的普通文件。
• 在通知区域点一下、按快捷键（Ctrl+Alt+R），或在通话开始时自动录制。
• 待办事项会与实际说过的话逐一核对，不会凭空编造。
• 给同事的声音命名一次，之后的会议中 Hesper 就能认出他。
• 对所有会议提问，获得附带来源的回答。
• 经典版 Outlook 日历（或日历链接）为会议及其参与者命名。
• 应用支持中文、英语、法语、西班牙语、葡萄牙语和日语；笔记使用会议的语言撰写。

首次启动时，Hesper 会下载其 AI 引擎和模型（约 4–6 GB，仅一次）；之后即可离线使用。需要 Windows 10 或 11，建议 16 GB 内存，约 8 GB 可用磁盘空间。

免费开源（MIT）：https://github.com/Gbally/Hesper

录音前请务必告知其他参与者，并遵守你所在公司的规定和当地法律。
```

**Short description**

```
完全离线的私密会议笔记：Hesper 录制你的通话，由在你电脑上运行的 AI 写出摘要、决定和待办事项。
```

**Product features**

```
录制你的麦克风和电脑声音：Teams、Zoom、Google Meet、Webex 等
摘要、决定、待解决的问题，以及带负责人和截止日期的待办事项
完全在你的电脑上运行：无需云端、账户或订阅
使用 NVIDIA 显卡时速度更快
分辨谁在说话，并记住你命名过的声音
对所有会议提问，并显示来源
所有会议的待办事项汇总在一个清单里
会议类型：通用、一对一、站会、客户电话、面试、研讨会
经典版 Outlook 日历或日历链接为会议和参与者命名
将笔记作为草稿发送到你的邮件应用、Outlook 或 Gmail
保存为你所选文件夹中的普通文件；音频默认 7 天后删除
浅色和深色，支持六种语言
免费开源（MIT）
```

**Screenshot captions**

1. 每次会议后的笔记：摘要、决定，以及带负责人和截止日期的待办事项。
2. 在通知区域开始录制，并查看所有属于你的待办事项。
3. 能分辨说话人的转写，以及来自你会议内容的回答。
4. 浅色或深色，与 Windows 一致。

**Search terms**

```
会议笔记
语音转写
会议记录
会议录音
离线 AI
语音转文字
待办清单
```
