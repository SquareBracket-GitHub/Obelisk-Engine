# Obelisk Engine v0.1 개발 계획

## 1. 문서 목적

이 문서는 Obelisk Engine의 최초 공개 가능한 버전인 **v0.1**을 6주 안에 개발하기 위한 실행 계획이다.

v0.1의 목표는 많은 기능을 제공하는 것이 아니다. 다음 흐름이 처음부터 끝까지 실제로 동작하는 최소 엔진을 완성하는 것이 목표다.

```text
프로젝트 생성
    ↓
게임 코드 작성
    ↓
개발 실행
    ↓
Plugin 추가
    ↓
게임 빌드
    ↓
Windows에서 배포·실행
```

핵심 성공 기준은 Obelisk만으로 작은 2D 게임을 완성할 수 있고, 게임 구조를 강제하지 않으며, Core 외 기능을 Plugin으로 확장할 수 있는 것이다.

---

## 2. 일정과 전제

- 개발 기간: 6주
- 시작일: 2026-10-05
- 목표 완료일: 2026-11-14
- 작업일: 주 6일, 총 36일
- 정기 휴일: 매주 일요일
- 우선 플랫폼: Windows x64
- 개발 방식: 1인 풀타임 + AI 보조
- 런타임: .NET 10 LTS
- 플랫폼 백엔드: SDL3
- 그래픽 백엔드: SDL_GPU
- C# 바인딩 후보: SDL3-CS
- 개발 우선순위: 동작하는 수직 슬라이스 > 기능 수 > 최적화

Linux와 macOS를 고려하여 API 경계를 설계하지만, v0.1 릴리스 검증은 Windows에 집중한다.

---

## 3. v0.1 범위

### Vanilla

- Application lifecycle
- Game loop
- Delta Time
- Window 생성과 종료
- 창 크기 변경
- Keyboard input
- Mouse input
- 기본 2D 렌더링
- Rectangle, Line, Circle
- Texture와 Sprite
- Text와 Font
- 기본 Audio 재생
- Asset 탐색과 캐시
- 기본 수학 및 색상 타입
- 오류 처리와 로그

### CLI

```text
obelisk new
obelisk run
obelisk dev
obelisk build
obelisk add <local-path>
obelisk remove <plugin>
obelisk list
obelisk info
```

### Plugin

- Plugin 계약
- 시작 시점 Module 등록
- `obelisk.plugin.json`
- 로컬 Plugin 설치와 제거
- Plugin 전용 assets, shaders, native 파일
- 엔진 버전 호환성 검사
- Update/Render/Asset 확장 지점의 최소 형태
- 예제 Plugin 1개

---

## 4. 아키텍처 초안

```text
Obelisk
├── src/
│   ├── Obelisk.Abstractions/
│   ├── Obelisk.Runtime/
│   ├── Obelisk.Platform.Sdl3/
│   ├── Obelisk.Graphics2D/
│   ├── Obelisk.Assets/
│   ├── Obelisk.Plugin/
│   ├── Obelisk.Vanilla/
│   └── Obelisk.Cli/
├── samples/
│   ├── MovingRectangle/
│   ├── Pong/
│   └── SamplePlugin/
├── tests/
├── docs/
└── PLAN.md
```

### 패키지 책임

| 패키지 | 책임 |
|---|---|
| `Obelisk.Abstractions` | 공개 계약, 핸들, 공용 값 타입 |
| `Obelisk.Runtime` | 앱 수명주기, 루프, 시간, Module 호스트 |
| `Obelisk.Platform.Sdl3` | 창, 이벤트, 입력, 오디오 장치의 SDL3 구현 |
| `Obelisk.Graphics2D` | Canvas, Texture, Font, 2D 명령 수집과 배칭 |
| `Obelisk.Assets` | 경로 해석, Asset loader, 캐시와 수명 |
| `Obelisk.Plugin` | Plugin 계약, manifest, 로딩과 호환성 검사 |
| `Obelisk.Vanilla` | 사용자에게 기본 기능을 묶어 제공하는 메타 패키지 |
| `Obelisk.Cli` | 프로젝트 생성, 실행, 빌드, Plugin 관리 |

### 경계 원칙

1. 게임 코드와 공개 API에 SDL 타입을 노출하지 않는다.
2. Runtime은 실제 창이나 GPU 없이 테스트할 수 있어야 한다.
3. `System.Numerics`의 벡터와 행렬을 우선 사용한다.
4. Obelisk는 `Color`, `RectF`, `SizeI`처럼 필요한 값 타입만 정의한다.
5. Update와 Render는 기본적으로 메인 스레드에서 순차 실행한다.
6. Plugin 배포 단위와 실행 단위인 Module을 구분한다.
7. 아직 안정화되지 않은 저수준 그래픽 확장은 Experimental API로 둔다.

---

## 5. 공개 API 초안

게임은 엔진이 요구하는 최소 수명주기만 구현한다. Scene, Entity, Component 등의 구조는 요구하지 않는다.

```csharp
public interface IGame
{
    void Initialize(InitializeContext context);
    void Update(UpdateContext context);
    void Render(RenderContext context);
    void Shutdown(ShutdownContext context);
}
```

```csharp
return ObeliskApp.Run(
    new MyGame(),
    new AppOptions
    {
        Title = "My Game",
        Width = 1280,
        Height = 720
    });
```

입력과 렌더링은 테스트와 상태 격리를 위해 인스턴스 Context를 기본 API로 사용한다.

```csharp
public void Update(UpdateContext context)
{
    if (context.Input.Keyboard.IsDown(Key.Right))
        _position.X += 200f * context.Time.Delta;
}

public void Render(RenderContext context)
{
    context.Canvas.Clear(Color.Black);
    context.Canvas.FillRectangle(
        new RectF(_position, new Vector2(64, 64)),
        Color.White);
}
```

정적 `Input`, `Draw`, `Audio` API는 v0.1 안정 API에 포함하지 않는다. 필요성이 검증되면 이후 편의 계층으로 추가한다.

---

## 6. 6주 실행 계획

## 1주차 — 실행 가능한 엔진 골격

기간: 2026-10-05 ~ 2026-10-11

### 목표

검은 창을 열고, 키보드로 흰 사각형을 움직이고, 정상적으로 종료한다.

### 작업

- .NET 10 SDK 개발 환경 준비
- 저장소와 Solution 생성
- 프로젝트 및 테스트 구조 생성
- Architecture Decision Record 작성 시작
- `IGame`과 Context 계약 정의
- Application lifecycle 구현
- 단조 시계 기반 Game loop와 Delta Time 구현
- SDL3 초기화와 Window 생성
- 종료 이벤트 처리
- Keyboard/Mouse 입력 스냅샷
- SDL_GPU 초기화
- 화면 Clear와 단색 사각형 렌더링
- `MovingRectangle` 샘플
- CI에서 빌드와 단위 테스트 수행

### 일별 로드맵

체크 표기: `[ ]` 예정 또는 진행 중, `[x]` 완료.

#### 1일차 — 10-05 월

- [x] .NET 10 SDK를 설치한다.
- [x] `global.json`으로 SDK 버전을 고정한다.
- [x] Git 저장소와 `Obelisk.sln`을 생성한다.
- [x] `src`, `samples`, `tests` 프로젝트 골격과 참조 관계를 구성한다.
- [x] 전체 Solution을 경고와 오류 없이 빌드한다.
- [x] 테스트 프로젝트를 실행하고 스모크 테스트가 통과하는지 확인한다.

#### 2일차 — 10-06 화

- [x] `IGame`과 Initialize, Update, Render, Shutdown Context 계약을 정의한다.
- [x] Application lifecycle 상태와 실행 순서를 구현한다.
- [x] 단조 시계 기반 Time 계약과 Delta Time 계산을 구현한다.
- [x] 실제 창 없이 실행할 수 있는 가짜 플랫폼 경계를 만든다.
- [x] Initialize → Update/Render → Shutdown 순서를 테스트한다.
- [x] Delta Time 계산과 제한 동작을 테스트한다.

#### 3일차 — 10-07 수

- [x] SDL3 C# 바인딩 후보를 검증하고 의존성을 추가한다.
- [x] SDL3 초기화와 종료 처리를 구현한다.
- [x] Window 생성과 이벤트 펌프를 구현한다.
- [x] 창 닫기 이벤트와 Escape 종료를 연결한다.
- [x] 검은 창이 열리고 두 종료 경로가 오류 없이 동작하는지 확인한다.

#### 4일차 — 10-08 목

- [ ] 프레임 단위 Keyboard 입력 스냅샷을 구현한다.
- [ ] 프레임 단위 Mouse 입력 스냅샷과 위치를 구현한다.
- [ ] Down, Pressed, Released 상태 전이를 정의한다.
- [ ] 입력 상태가 다음 프레임으로 올바르게 전이되는지 테스트한다.
- [ ] 마우스 위치와 버튼 상태를 테스트한다.

#### 5일차 — 10-09 금

- [ ] SDL_GPU 장치와 swapchain 초기화를 기술 검증한다.
- [ ] 화면 Clear 경로를 구현한다.
- [ ] 단색 사각형을 위한 최소 GPU pipeline을 구현한다.
- [ ] 오류 발생 시 진단 가능한 초기화 메시지를 제공한다.
- [ ] 창에 배경색과 단색 사각형 하나가 표시되는지 확인한다.

#### 6일차 — 10-10 토

- [ ] `MovingRectangle` 샘플을 Runtime, 입력, 렌더링과 통합한다.
- [ ] 방향키 이동에 Delta Time을 적용한다.
- [ ] Window resize 경로를 구현하고 검증한다.
- [ ] 단위 테스트와 통합 테스트를 정리한다.
- [ ] CI에서 Solution 빌드와 테스트를 실행한다.
- [ ] 1주차 완료 조건을 모두 재검증한다.

#### 휴일 — 10-11 일

- 계획된 개발 작업 없음.

### 완료 조건

- 창이 안정적으로 열리고 닫힌다.
- Escape와 창 닫기 버튼으로 정상 종료한다.
- 방향키로 사각형이 프레임 속도와 무관하게 움직인다.
- 창 크기를 변경해도 렌더링이 계속된다.
- 게임 프로젝트는 SDL3를 직접 참조하지 않는다.
- Runtime 핵심 테스트가 실제 창 없이 실행된다.

---

## 2주차 — 기본 2D 렌더링

기간: 2026-10-12 ~ 2026-10-18

### 목표

간단한 2D 게임 화면을 그리는 데 필요한 그래픽 기반을 완성한다.

### 작업

- 2D 렌더 명령 수집 구조
- GPU buffer와 resource 수명 관리
- Sprite batching
- Rectangle, Line, Circle
- Texture 생성, 업로드, 해제
- 이미지 파일 로딩
- 위치, 크기, 회전, 원점, 색상 변조
- Blend mode의 최소 지원
- Viewport와 창 크기 변경 처리
- GPU 오류와 장치 초기화 실패 메시지
- 렌더링 단위 테스트가 가능한 명령 검증 계층

### 일별 로드맵

#### 7일차 — 10-12 월

- [ ] Canvas 공개 계약과 좌표계 규칙을 정의한다.
- [ ] Rectangle, Line, Circle, Sprite용 2D Render Command를 정의한다.
- [ ] 명령 수집 계층이 그래픽 백엔드와 분리되도록 구성한다.
- [ ] 게임 코드가 백엔드 타입 없이 명령을 생성하는지 테스트한다.

#### 8일차 — 10-13 화

- [ ] Quad 렌더링 pipeline과 shader를 구현한다.
- [ ] Vertex와 Index buffer의 생성 및 갱신 경로를 구현한다.
- [ ] GPU resource wrapper와 명시적 해제 정책을 정의한다.
- [ ] 여러 프레임에서 buffer와 pipeline이 안전하게 재사용되는지 검증한다.

#### 9일차 — 10-14 수

- [ ] 이미지 파일 디코딩 경로를 구현한다.
- [ ] Texture 생성과 GPU 업로드를 구현한다.
- [ ] Texture 해제와 중복 해제 방지 동작을 구현한다.
- [ ] 파일에서 읽은 Texture가 화면에 표시되는지 확인한다.
- [ ] Texture를 명시적으로 해제할 수 있는지 검증한다.

#### 10일차 — 10-15 목

- [ ] Sprite batching 기준과 batch flush 조건을 정의한다.
- [ ] 위치, 크기, 회전, 원점 transform을 구현한다.
- [ ] 색상 변조와 최소 Blend mode를 구현한다.
- [ ] 여러 Sprite가 배치로 그려지는지 검증한다.
- [ ] transform, origin, color, blend 결과를 확인한다.

#### 11일차 — 10-16 금

- [ ] Rectangle, Line, Circle 렌더링을 구현한다.
- [ ] Viewport와 출력 크기 변경 처리를 구현한다.
- [ ] 창 최소화와 복원 시 렌더링 중단·재개를 처리한다.
- [ ] 기본 도형과 resize 경로가 함께 동작하는지 확인한다.

#### 12일차 — 10-17 토

- [ ] GPU 초기화와 렌더 실패 메시지를 정리한다.
- [ ] GPU resource 반복 생성·해제 시나리오를 검증한다.
- [ ] 렌더 명령 단위 테스트를 정리한다.
- [ ] 2주차 Sample 코드를 공개 API 기준으로 정리한다.
- [ ] 2주차 완료 조건을 모두 재검증한다.

#### 휴일 — 10-18 일

- 계획된 개발 작업 없음.

### 완료 조건

- 다수의 Sprite와 도형을 한 프레임에 그릴 수 있다.
- Texture 리소스가 중복 생성되거나 누수되지 않는다.
- 크기 변경과 최소화/복원 후 화면이 정상 복구된다.
- 좌표계는 좌측 상단 원점, +Y 아래 방향으로 일관된다.

---

## 3주차 — Vanilla MVP

기간: 2026-10-19 ~ 2026-10-25

### 목표

외부 Plugin 없이 작은 2D 게임 하나를 처음부터 끝까지 완성한다.

### 작업

- Font와 Text 렌더링
- WAV/OGG 기본 재생
- Sound와 Music 구분
- 재생, 정지, 반복, 볼륨
- Asset 경로와 URI 규칙
- Asset loader 등록
- Asset 캐시와 명시적인 수명 정책
- 로그와 진단 메시지
- Pong 또는 Breakout 완성
- Vanilla API 사용성 개선
- Quick Start 문서 초안

### 일별 로드맵

#### 13일차 — 10-19 월

- [ ] Asset URI와 프로젝트·Plugin 경로 규칙을 정의한다.
- [ ] 경로 정규화와 프로젝트 루트 이탈 방지를 구현한다.
- [ ] Asset loader 등록 기반을 구현한다.
- [ ] Asset cache와 수명 정책의 최소 형태를 구현한다.
- [ ] 동일 Asset 재요청이 cache를 사용하는지 테스트한다.
- [ ] 잘못된 경로가 원인을 포함한 오류를 내는지 테스트한다.

#### 14일차 — 10-20 화

- [ ] Font 로딩과 해제 경로를 구현한다.
- [ ] Glyph rasterization과 atlas 관리를 구현한다.
- [ ] Text 측정과 렌더 명령을 구현한다.
- [ ] 기본 Latin 문자열을 지정 Font로 표시한다.
- [ ] Korean 샘플 문자열을 지정 Font로 표시한다.

#### 15일차 — 10-21 수

- [ ] Audio 장치 초기화와 종료 처리를 구현한다.
- [ ] WAV Sound 로딩과 재생을 구현한다.
- [ ] Sound 정지와 볼륨 제어를 구현한다.
- [ ] Audio resource 수명과 해제 정책을 적용한다.
- [ ] 오디오 장치가 없는 환경을 안전하게 처리하는지 검증한다.

#### 16일차 — 10-22 목

- [ ] Sound와 Music의 책임과 API를 구분한다.
- [ ] OGG Music 로딩과 스트리밍 또는 재생 경로를 구현한다.
- [ ] Loop와 전역·개별 볼륨 제어를 구현한다.
- [ ] 배경음악과 효과음을 동시에 재생하고 각각 제어할 수 있는지 확인한다.

#### 17일차 — 10-23 금

- [ ] Pong 또는 Breakout의 게임 규칙과 상태를 구현한다.
- [ ] 타이틀과 게임 시작 흐름을 구현한다.
- [ ] 플레이와 충돌·점수 흐름을 구현한다.
- [ ] 승패와 재시작 흐름을 구현한다.
- [ ] 작은 게임을 처음부터 끝까지 완주할 수 있는지 확인한다.

#### 18일차 — 10-24 토

- [ ] 게임에 이미지, 텍스트, 효과음, 배경음악을 통합한다.
- [ ] 실제 Sample 사용 경험을 바탕으로 Vanilla API를 다듬는다.
- [ ] 오류 메시지와 로그를 정리한다.
- [ ] Quick Start 문서 초안을 작성한다.
- [ ] Debug와 Release에서 Sample을 실행한다.
- [ ] 3주차 완료 조건과 Vanilla MVP Gate를 모두 통과한다.

#### 휴일 — 10-25 일

- 계획된 개발 작업 없음.

### 완료 조건

- Vanilla만으로 Pong 또는 Breakout을 완성한다.
- 이미지, 텍스트, 효과음, 배경음악이 함께 동작한다.
- 같은 Asset의 반복 요청이 캐시를 사용한다.
- 누락되거나 잘못된 Asset 오류가 경로와 원인을 표시한다.
- Debug와 Release 구성 모두 실행된다.

### 마일스톤

**Vanilla MVP 완료**

---

## 4주차 — Plugin 기반

기간: 2026-10-26 ~ 2026-11-01

### 목표

Core를 수정하지 않고 기능을 추가할 수 있는 최초의 Plugin 경로를 완성한다.

### 작업

- Plugin과 Module의 책임 정의
- `IObeliskPlugin` 계약
- Module 등록과 수명주기
- `obelisk.plugin.json` 스키마
- 시작 시점 Assembly 로딩
- Plugin 전용 assets, shaders, native 경로
- 엔진 및 Plugin 버전 호환성 검사
- Plugin 중복과 누락 의존성 오류
- Update hook
- 제한적인 Render hook
- Asset loader 확장 지점
- `SamplePlugin` 구현
- Plugin 작성 가이드 초안

### 일별 로드맵

#### 19일차 — 10-26 월

- [ ] Plugin 배포 단위와 Module 실행 단위의 책임을 정의한다.
- [ ] `IObeliskPlugin` 최소 계약을 정의한다.
- [ ] Module 등록 API와 lifecycle을 정의한다.
- [ ] Plugin과 Module 설계 결정을 ADR로 기록한다.
- [ ] 공개 계약과 등록 순서를 단위 테스트로 고정한다.

#### 20일차 — 10-27 화

- [ ] `obelisk.plugin.json` 스키마를 정의한다.
- [ ] Manifest parser와 필수 필드 검증을 구현한다.
- [ ] 엔진과 Plugin 버전 호환성 검사를 구현한다.
- [ ] 정상, 누락, 중복, 비호환 manifest 사례를 테스트한다.
- [ ] 거부 오류가 Plugin 이름과 원인을 설명하는지 확인한다.

#### 21일차 — 10-28 수

- [ ] 프로젝트에 지정된 로컬 Plugin 검색 규칙을 구현한다.
- [ ] 시작 시점 Assembly 로더를 구현한다.
- [ ] Plugin 진입점 발견과 인스턴스 생성을 구현한다.
- [ ] 동일 Plugin을 시작 시 한 번만 로드하는지 테스트한다.
- [ ] 로딩 실패가 다른 진단 정보를 잃지 않는지 확인한다.

#### 22일차 — 10-29 목

- [ ] Plugin별 assets 경로를 해석한다.
- [ ] Plugin별 shaders 경로를 해석한다.
- [ ] Plugin별 native 파일 경계를 해석한다.
- [ ] Plugin 실패가 가능한 범위에서 Core 실행과 격리되도록 처리한다.
- [ ] 오류에 Plugin 이름과 대상 경로가 포함되는지 테스트한다.

#### 23일차 — 10-30 금

- [ ] Plugin Update hook을 구현한다.
- [ ] Plugin Asset loader 등록 지점을 구현한다.
- [ ] 제한적인 Render hook을 구현한다.
- [ ] SamplePlugin이 Core 수정 없이 실행 흐름에 참여하는지 확인한다.
- [ ] SamplePlugin이 자체 Asset loader를 등록해 사용하는지 확인한다.

#### 24일차 — 10-31 토

- [ ] 공개 예제로 사용할 SamplePlugin을 정리한다.
- [ ] Plugin 설치, 사용, 제거 시나리오를 통합 테스트한다.
- [ ] 비호환 Plugin 거부 시나리오를 통합 테스트한다.
- [ ] Plugin 작성 가이드 초안을 작성한다.
- [ ] 4주차 완료 조건을 모두 재검증한다.

#### 휴일 — 11-01 일

- 계획된 개발 작업 없음.

### 완료 조건

- 로컬 Plugin을 설치하고 게임에서 사용할 수 있다.
- Plugin 제거 후 Core와 게임이 정상 동작한다.
- 호환되지 않는 엔진 버전을 명확한 오류로 거부한다.
- Plugin 코드가 SDL 타입이나 내부 Runtime 구현에 의존하지 않는다.
- Plugin 실패가 가능한 한 Plugin 이름과 원인을 표시한다.

---

## 5주차 — CLI와 빌드 흐름

기간: 2026-11-02 ~ 2026-11-08

### 목표

프로젝트 생성부터 개발, Plugin 관리, 배포 빌드까지 CLI로 연결한다.

### 작업

- .NET Tool 형태의 `Obelisk.Cli`
- `obelisk new <name>`
- `obelisk run`
- `obelisk dev`
- 파일 변경 감지와 프로세스 재시작
- `obelisk build`
- Windows x64 배포 폴더 생성
- `obelisk add <local-path>`
- `obelisk remove <plugin>`
- `obelisk list`
- `obelisk info`
- `obelisk.json` 스키마와 검증
- Native dependency 복사
- CLI 오류 코드와 사용자 메시지
- CLI 통합 테스트

### 일별 로드맵

#### 25일차 — 11-02 월

- [ ] `Obelisk.Cli`를 설치 가능한 .NET Tool로 구성한다.
- [ ] 실행 명령 이름을 `obelisk`로 고정한다.
- [ ] 명령 router와 공통 실행 Context를 구현한다.
- [ ] `obelisk --help`와 `obelisk info`를 구현한다.
- [ ] 성공과 주요 실패 유형의 종료 코드를 정의한다.
- [ ] 전역 도구로 설치한 뒤 저장소 밖에서 `obelisk`가 실행되는지 확인한다.

#### 26일차 — 11-03 화

- [ ] `obelisk new <name>` 명령과 프로젝트 이름 검증을 구현한다.
- [ ] CLI에 버전이 고정된 게임 프로젝트 템플릿을 포함한다.
- [ ] `Program.cs`, 게임 클래스, `.csproj`, `obelisk.json`, `assets` 구조를 생성한다.
- [ ] 생성 프로젝트가 `Obelisk.Vanilla` NuGet 패키지를 참조하도록 구성한다.
- [ ] 대상 디렉터리 충돌과 부분 생성 실패를 안전하게 처리한다.
- [ ] 빈 작업 공간에서 `obelisk new MyGame` 결과가 복원·빌드되는지 테스트한다.

#### 27일차 — 11-04 수

- [ ] `obelisk.json` 읽기와 스키마 검증을 구현한다.
- [ ] `obelisk run`으로 생성 프로젝트를 복원하고 실행한다.
- [ ] CLI 뒤의 게임 인수를 자식 프로세스로 전달한다.
- [ ] 생성 직후 `obelisk run`으로 게임이 실행되는지 확인한다.
- [ ] 잘못된 디렉터리와 manifest를 해결 가능한 메시지로 거부한다.

#### 28일차 — 11-05 목

- [ ] `obelisk dev`의 파일 변경 감지를 구현한다.
- [ ] 변경 이벤트 debounce와 불필요한 재시작 억제를 구현한다.
- [ ] 기존 자식 프로세스를 안전하게 종료한 뒤 재실행한다.
- [ ] CLI 종료 시 자식 프로세스가 남지 않도록 처리한다.
- [ ] 코드 변경 후 게임이 자동 재실행되고 프로세스가 중복되지 않는지 검증한다.

#### 29일차 — 11-06 금

- [ ] `obelisk build` 명령과 출력 디렉터리 규칙을 구현한다.
- [ ] Windows x64 self-contained publish를 실행한다.
- [ ] SDL3 등 필요한 native dependency를 배포 폴더에 복사한다.
- [ ] assets와 Plugin 파일을 배포 구조에 포함한다.
- [ ] 개발 SDK가 없는 Windows 환경용 결과물이 생성되는지 검증한다.

#### 30일차 — 11-07 토

- [ ] `obelisk add <local-path>`를 구현한다.
- [ ] `obelisk remove <plugin>`을 구현한다.
- [ ] `obelisk list`를 구현한다.
- [ ] Plugin 설치·목록·제거가 재현 가능한지 통합 테스트한다.
- [ ] 저장소 밖 임시 디렉터리에서 `new → run → add → build` 흐름을 테스트한다.
- [ ] 5주차 CLI 완료 조건을 모두 재검증한다.

#### 휴일 — 11-08 일

- 계획된 개발 작업 없음.

### 완료 조건

- 새 디렉터리에서 세 명령 안에 샘플 게임을 실행할 수 있다.
- 생성된 프로젝트를 수정하면 `obelisk dev`가 재실행한다.
- 로컬 Plugin의 설치와 제거가 재현 가능하다.
- `obelisk build` 결과를 개발 SDK가 없는 Windows PC에서 실행할 수 있다.
- 실패한 명령은 0이 아닌 종료 코드와 해결 가능한 메시지를 제공한다.

---

## 6주차 — 통합, 안정화, v0.1 릴리스

기간: 2026-11-09 ~ 2026-11-15

### 목표

전체 개발 흐름을 검증하고 재현 가능한 v0.1 패키지를 만든다.

### 작업

- MovingRectangle 샘플 정리
- Pong 또는 Breakout 샘플 정리
- SamplePlugin 정리
- 전체 API 일관성 검토
- 공개 API XML documentation
- Getting Started
- Plugin Authoring Guide
- CLI Reference
- Native dependency 및 라이선스 검토
- 메모리와 GPU 리소스 누수 점검
- 장시간 실행 및 창 복원 테스트
- 깨끗한 환경에서 생성/실행/빌드 검증
- NuGet 및 CLI 패키지 생성
- 릴리스 노트와 알려진 제한 작성
- `v0.1.0` 태그 후보 생성

### 일별 로드맵

#### 31일차 — 11-09 월

- [ ] MovingRectangle Sample을 동결 후보 API에 맞춰 정리한다.
- [ ] 완성 게임 Sample을 동결 후보 API에 맞춰 정리한다.
- [ ] SamplePlugin을 동결 후보 API에 맞춰 정리한다.
- [ ] 공개 API의 이름, namespace, 오류 모델을 검토한다.
- [ ] 세 Sample이 동일한 동결 후보 API로 Debug와 Release에서 빌드되는지 확인한다.

#### 32일차 — 11-10 화

- [ ] 공개 API에 XML documentation을 작성한다.
- [ ] CLI 설치부터 `obelisk new`, `obelisk run`까지 Getting Started를 작성한다.
- [ ] Plugin Authoring Guide를 완성한다.
- [ ] CLI Reference와 종료 코드 문서를 작성한다.
- [ ] 새 사용자가 문서만 보고 게임과 Plugin을 생성할 수 있는지 검토한다.

#### 33일차 — 11-11 수

- [ ] 전체 단위 테스트와 통합 테스트를 실행한다.
- [ ] 빈 임시 디렉터리에 배포된 CLI를 설치한다.
- [ ] `obelisk new`로 저장소와 독립된 프로젝트를 생성한다.
- [ ] 생성 프로젝트에서 `run → add → build` 자동 시나리오를 실행한다.
- [ ] 동일한 전체 흐름을 수동 체크리스트로 검증한다.
- [ ] Debug와 Release Sample 회귀 테스트를 실행한다.

#### 34일차 — 11-12 목

- [ ] 게임을 30분 이상 반복 실행한다.
- [ ] CPU, 메모리, GPU resource 누수를 점검한다.
- [ ] 창 최소화, 복원, resize 반복 동작을 검증한다.
- [ ] 종료 시 충돌과 남은 프로세스가 없는지 확인한다.
- [ ] native dependency와 배포 라이선스를 검토한다.
- [ ] 배포 파일에 포함할 라이선스 목록을 작성한다.

#### 35일차 — 11-13 금

- [ ] 엔진 NuGet 패키지를 재현 가능하게 생성한다.
- [ ] 설치 가능한 CLI Tool 패키지를 생성한다.
- [ ] Windows x64용 standalone CLI 배포 형식을 검증한다.
- [ ] Release Candidate 패키지와 checksum을 생성한다.
- [ ] 깨끗한 환경에서 CLI 설치와 프로젝트 생성 Smoke Test를 실행한다.
- [ ] SDK 없는 Windows 환경에서 게임 배포 결과를 실행한다.

#### 36일차 — 11-14 토

- [ ] Release Candidate에서 발견된 결함을 수정한다.
- [ ] 전체 자동 테스트와 Sample 회귀 테스트를 다시 실행한다.
- [ ] `new → run → add → build → 배포 실행` 흐름을 최종 검증한다.
- [ ] 릴리스 노트와 알려진 제한을 작성한다.
- [ ] 지원 OS, 엔진 호환 범위, Plugin 호환 범위를 명시한다.
- [ ] 모든 v0.1 완료 조건을 재검증한다.
- [ ] `v0.1.0` 태그 후보를 확정한다.

#### 휴일 — 11-15 일

- 계획된 개발 작업 없음.

### 완료 조건

- `new → run → add plugin → build → 배포 실행` 흐름이 통과한다.
- 제공 샘플이 모두 Debug와 Release에서 실행된다.
- 자동화된 테스트와 패키지 검증이 통과한다.
- 치명적인 리소스 누수나 종료 충돌이 없다.
- 문서만 보고 새 프로젝트와 Plugin을 만들 수 있다.
- 알려진 제한과 호환 범위가 문서화되어 있다.

### 마일스톤

**Obelisk Engine v0.1 완료**

---

## 7. v0.1 완료 정의

다음 조건을 모두 만족해야 v0.1을 완료로 본다.

### 사용자 경험

- `obelisk new MyGame`으로 프로젝트가 생성된다.
- `obelisk run`으로 게임이 실행된다.
- Vanilla API만으로 완성된 작은 2D 게임이 존재한다.
- `obelisk add <local-path>`로 Plugin을 사용할 수 있다.
- `obelisk build`로 배포 가능한 결과가 생성된다.

### 아키텍처

- 공개 API가 특정 게임 오브젝트 모델을 강제하지 않는다.
- 공개 API에 SDL 타입이 노출되지 않는다.
- Runtime, 플랫폼, 그래픽, Asset, Plugin, CLI 경계가 분리되어 있다.
- 게임과 Plugin은 내부 구현 프로젝트를 참조하지 않는다.

### 품질

- 핵심 Runtime과 CLI에 자동화된 테스트가 있다.
- Sample이 회귀 테스트 역할을 한다.
- 리소스 생성과 해제 정책이 문서화되어 있다.
- 오류에 대상 경로, Plugin 또는 하위 시스템 정보가 포함된다.
- 지원 OS와 알려진 제한이 명확하다.

---

## 8. v0.1에서 명시적으로 제외하는 기능

아래 기능은 6주 일정을 지키기 위해 구현하지 않는다.

- Physics
- Particle System
- Post Processing
- Scene, Entity, Component, Prefab
- UI Framework
- 3D
- Networking과 Multiplayer
- Gamepad
- 여러 Window
- 실행 중 Plugin Hot Reload
- Plugin Assembly 언로드
- 원격 Package Registry
- Plugin 서명과 Sandbox
- Plugin 의존성의 범용 SAT 해결
- 안정화된 저수준 GPU Plugin API
- 복잡한 Render Graph
- 자체 Shader 언어
- 모바일과 WebAssembly
- Linux/macOS 릴리스 인증
- Editor와 Inspector

제외된 기능이 필요하면 Core에 임시로 넣지 않고 v0.2 이후 작업 또는 별도 Plugin으로 기록한다.

---

## 9. 범위 변경 규칙

새 기능을 v0.1에 넣으려면 다음 질문에 모두 답해야 한다.

1. 현재 수직 슬라이스를 완성하는 데 반드시 필요한가?
2. 없으면 샘플 게임을 완성할 수 없는가?
3. Plugin이나 v0.2로 미룰 수 없는가?
4. 기존 주차 작업을 제거하지 않고도 일정을 지킬 수 있는가?
5. 특정 게임 구조를 강제하지 않는가?

하나라도 확신할 수 없다면 v0.1 범위에 추가하지 않는다.

일정이 밀릴 때는 기능의 완성도를 낮추기보다 다음 순서로 범위를 줄인다.

1. `obelisk dev`의 고급 파일 감지
2. Circle과 Line의 고급 옵션
3. OGG Music 지원
4. Render hook
5. Plugin의 native 자동 탐색

다음 항목은 줄이지 않는다.

- Window와 Game loop 안정성
- 입력 스냅샷의 정확성
- GPU 및 Asset 수명 관리
- 공개 API와 SDL 구현의 분리
- `new`, `run`, `build`
- 로컬 Plugin 로딩
- 최소 샘플 게임

---

## 10. 주요 위험과 대응

| 위험 | 영향 | 대응 |
|---|---|---|
| SDL3-CS API 또는 패키징 문제 | 초기 개발 지연 | 1주차 첫 이틀 안에 창과 GPU 스파이크를 완료하고 실패 시 바인딩 대안을 즉시 평가한다. |
| SDL_GPU 셰이더 및 Native 배포 복잡성 | 빌드 결과가 개발 PC 밖에서 실패 | 2주차부터 깨끗한 배포 폴더 테스트를 반복한다. |
| 텍스트 렌더링 범위 증가 | 3주차 일정 지연 | v0.1은 기본 Latin/Korean 표시와 단일 Font 흐름에 집중하고 고급 shaping은 제외한다. |
| Audio codec 범위 증가 | Vanilla MVP 지연 | WAV를 필수로 하고 OGG는 일정에 따라 축소할 수 있다. |
| Plugin API의 조기 일반화 | API 복잡도와 재작업 증가 | SamplePlugin이 실제로 요구하는 확장점만 공개한다. |
| CLI가 독자적인 빌드 시스템으로 커짐 | 5주차 일정 초과 | v0.1 CLI는 .NET build/publish를 조율하는 얇은 계층으로 유지한다. |
| 공개 API 변경 비용 | 샘플과 Plugin의 반복 수정 | 1~4주차 API는 Experimental로 취급하고 6주차에 v0.1 표면을 동결한다. |

---

## 11. 테스트 전략

### 단위 테스트

- Game loop 상태 전이
- Delta Time 제한과 누적
- 입력의 Down/Pressed/Released 전이
- Render command 생성
- Asset 경로 정규화와 캐시
- Plugin manifest 파싱과 호환성
- CLI argument 파싱과 오류 코드

### 통합 테스트

- SDL3 초기화와 종료
- 창 생성, 크기 변경, 최소화/복원
- GPU 리소스 생성과 해제
- Texture와 Font 로딩
- Audio 장치가 없는 환경의 처리
- Plugin 설치, 로딩, 제거
- 프로젝트 생성과 배포 빌드

### 수동 검증

- MovingRectangle
- Pong 또는 Breakout
- SamplePlugin 적용 전후 비교
- 개발 SDK가 없는 Windows 환경에서 실행
- 30분 이상 반복 실행

---

## 12. 개발 운영 규칙

- 매일 실행 가능한 Main branch 상태를 유지한다.
- 각 작업은 사용자에게 보이는 수직 기능 또는 검증 가능한 기반으로 끝낸다.
- 새 추상화는 두 번째 실제 사용 사례가 나타나기 전까지 최소화한다.
- 성능 최적화 전에 측정 가능한 기준을 만든다.
- Native handle은 백엔드 밖으로 유출하지 않는다.
- 샘플 코드는 임시 데모가 아니라 공개 API의 사용성 테스트로 취급한다.
- 중요한 설계 결정은 `docs/adr/`에 기록한다.
- 기능 완료에는 코드, 테스트, 최소 문서가 모두 포함된다.

---

## 13. 최초 착수 순서

1. .NET 10 SDK 설치와 버전 고정
2. Git 저장소 및 Solution 생성
3. SDL3-CS 창 생성 기술 검증
4. SDL_GPU 삼각형/사각형 기술 검증
5. 공개 API에서 SDL 타입 제거
6. MovingRectangle 수직 슬라이스 완성
7. 이후 2주차 그래픽 작업으로 진행

첫 기술 검증에서 SDL3-CS 또는 SDL_GPU가 치명적으로 막히면, 전체 구조를 바꾸기 전에 문제와 대안을 ADR로 기록하고 백엔드만 교체한다. 게임 API와 Runtime 계약은 유지하는 것을 우선한다.

