<a id="ja"></a>
**日本語** | [English](#en) | [한국어](#ko)

# QvPalette

> **本パッケージはQvPenの非公式拡張です。**
> 不具合の報告、ご質問、ご要望を、**QvPenの作者（ureishi氏）へ送ることは、絶対におやめください。**
> お問い合わせは、本パッケージの配布元へお願いします。

QvPen（v3.3.15）に追従する、インクパレット拡張です。QvPenのペン1本で、最大4096種類の色・マテリアルと、太さを切り替えられ、同期にも対応しています。

## 特長

- **ペン1本で全色・全太さ**: ペンの本数は、同時に描ける人数や、手元に置ける色の枠数を意味します。
- **絵の具パレット型の召喚・配置具**: ペンスタンドの横のパレット型のオブジェクトを掴んで好きな場所に置くと、その上にパネルが開きます。
- **VR / Desktop対応**: VRではペン先と人差し指で触れて操作、Desktopではマウスのクリックとドラッグで操作できます。
- **太さのスライダーとプリセット**: スライドに加えて、3.0 / 5.0 / 10.0 / 30.0mm（2ページ目は50 / 80 / 100 / 150mm）のプリセットがあります。
- **お気に入り8枠**: 色と太さをセットで登録できます。ワールドごとに、個人の設定として保存されます。
- **後から入室した人にも反映**: 台帳方式（色や太さを切り替えた点だけを記録）で同期します。
- **パレットエディタ**: インクごとにマテリアルとグラデーションを設定でき、ゲーム内に近いプレビュー（20 cmの線、太さの調整、PC/Questの切り替え）をその場で確認できます。

## 動作要件

| 項目 | 内容 |
|---|---|
| Unity | 2022.3 |
| VRChat SDK | Worlds 3.5以上（検証は3.10.6） |
| QvPen | 3.3.15（このバージョンで検証。他のバージョンは動作未確認） |
| TextMeshPro | 導入済みであること |
| 依存（自動で入ります） | QvInkPreview（`com.yakigura.qvinkpreview`） |

## 導入方法（VCC）

1. VCCの「Settings → Packages → Add Repository」に、`https://yakigura.github.io/vpm-qvpalette/index.json` を登録します（`https://yakigura.github.io/vpm-qvpalette/` の「VCCに追加する」からでも登録できます）。
2. プロジェクトの「Manage Project」で、**QvPalette** の「+」を押します。QvInkPreviewも一緒に導入されます。
3. シーンを開き、メニューの **`Tools > Yakigura > QvPalette > Setup（このシーンに導入・更新）`** を実行して、シーンを保存します。
4. シーン内の `QvPen_PenManager` が自動で検出され、台帳とパネルが生成されます。

## 使い方

- **ペンの本数を変えたとき、QvPaletteを更新したとき**: もう一度 `Setup（このシーンに導入・更新）` を実行します。
- **インクの設定**: `Tools > Yakigura > QvPalette > Palette Editor` を開き、インクごとのグラデーション、マテリアル（PC / Quest）、縁取りを設定します。ウィンドウ上部の「プレビュー」で見た目を確認できます。
- **台帳の容量**: シーンの `QvPalette/Setup` オブジェクトのインスペクターで、チャンク数と容量を調整します（既定は、ペン1本あたり4,096回で、十分な容量です）。

## 削除方法

シーン上の `QvPalette` のGameObjectを削除し、パッケージをプロジェクトから外します。QvPen側には、何も残りません。

## 既知の制限

1. **QvPenのバージョン**: 3.3.15で検証しています。将来のバージョンで色の同期の仕様が変わると、動作確認が必要です。
2. **旧版との併用禁止**: 過去の試作版（`QvPalet`、`QvPenInkPalette`、`TakadaQvPenInkPalette`）と、同じシーンに置かないでください。
3. **初期スタイル**: 起動時に、全ペンの初期スタイルが、パレットの0番に統一されます。
4. **描画中のプレビュー**: 描いている最中の線は、本人にしか見えません（QvPenの仕様です）。
5. **消しゴムの判定**: 当たり判定を作り直さない設定のため、初期の太さと変更後の太さの差の分だけ、わずかにずれます。
6. **お気に入り**: VRChatのPersistenceを使うので、ワールドごとの保存です。他のワールドには、持ち出せません。
7. **パネルの枚数**: ツールで開くパネルは1つだけです。ツールもワールドに1つで、右手でも左手でも使えますが、同時に使えるのは1つです。ペンの本数を増やしても、増えません。

## 利用規約

[LICENSE](LICENSE) を参照してください。ワールドへの組み込み・公開（商用ワールドを含む）と、自分のワールドに合わせた改変は自由です。再配布、改変版の公開、AIによる解析・学習への利用は禁止です。

## クレジット

- **QvPalette** — Developed by **Negiyaki**（やきたてぐらふてぃ）
- **QvPen** — Developed by **ureishi氏** / https://65536.booth.pm/items/1555789

---

<a id="en"></a>
[日本語](#ja) | **English** | [한국어](#ko)

# QvPalette

> **This package is an unofficial extension of QvPen.**
> **Please never send bug reports, questions, or requests about it to the author of QvPen (ureishi).**
> Please direct all inquiries to the distributor of this package.

An ink-palette extension that follows QvPen (v3.3.15). With a single QvPen pen, you can switch between up to 4096 colors/materials and widths, with sync support.

## Features

- **Every color and width on one pen**: The number of pens only determines how many people can draw at the same time, or how many color slots you can keep at hand.
- **Paint-palette-style summon/placement tool**: Grab the palette-shaped object next to the pen stand and place it wherever you like; a panel opens above it.
- **VR / Desktop support**: In VR, operate it by touching with the pen tip or your index finger; on Desktop, operate it by clicking and dragging with the mouse.
- **Width slider and presets**: In addition to the slider, there are presets of 3.0 / 5.0 / 10.0 / 30.0mm (page 2: 50 / 80 / 100 / 150mm).
- **8 favorite slots**: Register a color and width as a set. They are saved per world as personal settings.
- **Applied for late joiners too**: Syncs using a ledger (it records only the points where a color or width was switched).
- **Palette Editor**: Set a material and gradient for each ink, and check an in-game-like preview (a 20 cm stroke, width adjustment, PC/Quest switching) right there.

## Requirements

| Item | Details |
|---|---|
| Unity | 2022.3 |
| VRChat SDK | Worlds 3.5 or later (tested with 3.10.6) |
| QvPen | 3.3.15 (tested with this version; other versions are untested) |
| TextMeshPro | Must be installed |
| Dependency (installed automatically) | QvInkPreview (`com.yakigura.qvinkpreview`) |

## Installation (VCC)

1. In VCC, go to "Settings → Packages → Add Repository" and add `https://yakigura.github.io/vpm-qvpalette/index.json` (you can also add it with the "Add to VCC" button on `https://yakigura.github.io/vpm-qvpalette/`).
2. In your project's "Manage Project", press "+" for **QvPalette**. QvInkPreview is installed along with it.
3. Open your scene, run the menu item **`Tools > Yakigura > QvPalette > Setup（このシーンに導入・更新）`** (Install / update in this scene), and save the scene.
4. The `QvPen_PenManager` in the scene is detected automatically, and the ledger and panel are generated.

## Usage

- **When you change the number of pens, or after updating QvPalette**: Run `Setup（このシーンに導入・更新）` again.
- **Ink settings**: Open `Tools > Yakigura > QvPalette > Palette Editor` and set the gradient, materials (PC / Quest), and outline for each ink. You can check the look in the "Preview" at the top of the window.
- **Ledger capacity**: Adjust the number of chunks and the capacity in the Inspector of the `QvPalette/Setup` object in the scene (the default is 4,096 switches per pen, which is plenty).

## Uninstallation

Delete the `QvPalette` GameObject in the scene and remove the package from the project. Nothing is left behind on the QvPen side.

## Known limitations

1. **QvPen version**: Tested with 3.3.15. If a future version changes how color sync works, it will need to be re-verified.
2. **Do not use with old versions**: Do not place it in the same scene as past prototypes (`QvPalet`, `QvPenInkPalette`, `TakadaQvPenInkPalette`).
3. **Initial style**: At startup, the initial style of all pens is set to palette entry 0.
4. **Preview while drawing**: A stroke that is still being drawn is visible only to the person drawing it (this is how QvPen works).
5. **Eraser hit detection**: Because the colliders are not rebuilt, hit detection is slightly off by the difference between the initial width and the changed width.
6. **Favorites**: They use VRChat Persistence, so they are saved per world. They cannot be carried over to other worlds.
7. **Number of panels**: The tool opens only one panel. There is also only one tool per world; it can be used with either the right or left hand, but only one can be in use at a time. Adding more pens does not increase these.

## Terms of use

See [LICENSE](LICENSE). You are free to include it in worlds and publish them (including commercial worlds), and to modify it to fit your own world. Redistribution, publishing modified versions, and use for AI analysis or training are prohibited.

## Credits

- **QvPalette** — Developed by **Negiyaki** (やきたてぐらふてぃ)
- **QvPen** — Developed by **ureishi** / https://65536.booth.pm/items/1555789

---

<a id="ko"></a>
[日本語](#ja) | [English](#en) | **한국어**

# QvPalette

> **이 패키지는 QvPen의 비공식 확장입니다.**
> 버그 보고, 질문, 요청을 **QvPen의 제작자(ureishi 님)에게 보내는 일은 절대 삼가 주세요.**
> 문의는 이 패키지의 배포처로 부탁드립니다.

QvPen(v3.3.15)에 맞춰 동작하는 잉크 팔레트 확장입니다. QvPen의 펜 한 자루로 최대 4096종류의 색·머티리얼과 두께를 전환할 수 있으며, 동기화도 지원합니다.

## 특징

- **펜 한 자루로 모든 색·모든 두께**: 펜의 개수는 동시에 그릴 수 있는 인원수나, 손에 둘 수 있는 색 슬롯 수를 뜻합니다.
- **물감 팔레트형 소환·배치 도구**: 펜 스탠드 옆에 있는 팔레트 모양의 오브젝트를 잡아 원하는 곳에 두면, 그 위에 패널이 열립니다.
- **VR / Desktop 지원**: VR에서는 펜 끝이나 검지로 터치해서, Desktop에서는 마우스 클릭과 드래그로 조작할 수 있습니다.
- **두께 슬라이더와 프리셋**: 슬라이더 외에 3.0 / 5.0 / 10.0 / 30.0mm(2페이지는 50 / 80 / 100 / 150mm) 프리셋이 있습니다.
- **즐겨찾기 8칸**: 색과 두께를 세트로 등록할 수 있습니다. 월드별로 개인 설정으로 저장됩니다.
- **나중에 들어온 사람에게도 반영**: 장부 방식(색이나 두께를 전환한 지점만 기록)으로 동기화합니다.
- **팔레트 에디터**: 잉크마다 머티리얼과 그라디언트를 설정할 수 있고, 게임 내와 가까운 미리보기(20 cm 선, 두께 조정, PC/Quest 전환)를 그 자리에서 확인할 수 있습니다.

## 동작 요건

| 항목 | 내용 |
|---|---|
| Unity | 2022.3 |
| VRChat SDK | Worlds 3.5 이상(검증은 3.10.6) |
| QvPen | 3.3.15(이 버전으로 검증. 다른 버전은 동작 미확인) |
| TextMeshPro | 설치되어 있어야 함 |
| 의존성(자동으로 설치됨) | QvInkPreview(`com.yakigura.qvinkpreview`) |

## 설치 방법(VCC)

1. VCC의 「Settings → Packages → Add Repository」에 `https://yakigura.github.io/vpm-qvpalette/index.json` 을 등록합니다(`https://yakigura.github.io/vpm-qvpalette/` 의 「VCC에 추가」에서도 등록할 수 있습니다).
2. 프로젝트의 「Manage Project」에서 **QvPalette** 의 「+」를 누릅니다. QvInkPreview도 함께 설치됩니다.
3. 씬을 열고 메뉴의 **`Tools > Yakigura > QvPalette > Setup（このシーンに導入・更新）`**(이 씬에 설치·업데이트)를 실행한 뒤, 씬을 저장합니다.
4. 씬 안의 `QvPen_PenManager` 가 자동으로 감지되어, 장부와 패널이 생성됩니다.

## 사용법

- **펜 개수를 바꿨을 때, QvPalette를 업데이트했을 때**: `Setup（このシーンに導入・更新）` 을 다시 실행합니다.
- **잉크 설정**: `Tools > Yakigura > QvPalette > Palette Editor` 를 열고, 잉크마다 그라디언트, 머티리얼(PC / Quest), 테두리를 설정합니다. 창 위쪽의 「미리보기」에서 모양을 확인할 수 있습니다.
- **장부 용량**: 씬의 `QvPalette/Setup` 오브젝트의 인스펙터에서 청크 수와 용량을 조정합니다(기본값은 펜 한 자루당 4,096회로, 충분한 용량입니다).

## 삭제 방법

씬에 있는 `QvPalette` GameObject를 삭제하고, 패키지를 프로젝트에서 제거합니다. QvPen 쪽에는 아무것도 남지 않습니다.

## 알려진 제한

1. **QvPen 버전**: 3.3.15에서 검증했습니다. 향후 버전에서 색 동기화 사양이 바뀌면 동작 확인이 필요합니다.
2. **구버전과 함께 사용 금지**: 과거 시제품(`QvPalet`, `QvPenInkPalette`, `TakadaQvPenInkPalette`)과 같은 씬에 두지 마세요.
3. **초기 스타일**: 시작 시 모든 펜의 초기 스타일이 팔레트 0번으로 통일됩니다.
4. **그리는 중의 미리보기**: 그리고 있는 도중의 선은 본인에게만 보입니다(QvPen의 사양입니다).
5. **지우개 판정**: 충돌 판정을 다시 만들지 않는 설정이기 때문에, 초기 두께와 변경 후 두께의 차이만큼 약간 어긋납니다.
6. **즐겨찾기**: VRChat의 Persistence를 사용하므로 월드별로 저장됩니다. 다른 월드로는 가져갈 수 없습니다.
7. **패널 개수**: 도구로 여는 패널은 하나뿐입니다. 도구도 월드에 하나이며, 오른손으로도 왼손으로도 쓸 수 있지만 동시에 쓸 수 있는 것은 하나입니다. 펜 개수를 늘려도 늘어나지 않습니다.

## 이용 약관

[LICENSE](LICENSE) 를 참고해 주세요. 월드에 포함해 공개하는 것(상업용 월드 포함)과, 자신의 월드에 맞춘 개조는 자유입니다. 재배포, 개조판 공개, AI를 통한 분석·학습에 이용하는 것은 금지입니다.

## 크레딧

- **QvPalette** — Developed by **Negiyaki**(やきたてぐらふてぃ)
- **QvPen** — Developed by **ureishi 님** / https://65536.booth.pm/items/1555789
