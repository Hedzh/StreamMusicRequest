# Тест Yandex Music rotor/session

Это отдельный изолированный proof-of-concept для проверки нового механизма
Yandex Music rotor/session.

Тест не изменяет основную архитектуру SMR и не заменяет существующий
Tests/YandexMusicMyWaveTest.

## Что проверяет первый этап

1. Авторизацию Yandex Music.
2. Создание сессии через POST /rotor/session/new.
3. Использование seed user:onyourwave.
4. Получение radioSessionId.
5. Получение batchId.
6. Получение начальной sequence, если сервер её возвращает.

На первом этапе намеренно **не выполняются** feedback, воспроизведение и
запрос следующей пачки. Сначала нужно убедиться, что сам rotor/session
создаётся корректно на реальном аккаунте.

## Запуск

Из корня репозитория:

```powershell
dotnet run --project .\Tests\YandexMusicRotorSessionTest\YandexMusicRotorSessionTest.csproj
```

Если переменная окружения YANDEX_MUSIC_TOKEN не задана, тест предложит
пройти авторизацию через код устройства.

Можно заранее задать OAuth-токен:

```powershell
$env:YANDEX_MUSIC_TOKEN="ВАШ_ТОКЕН"
dotnet run --project .\Tests\YandexMusicRotorSessionTest\YandexMusicRotorSessionTest.csproj
```

Токен не записывается в файлы проекта.

## Что будем проверять дальше

Если создание session успешно, отдельными этапами проверим:

- radioStarted;
- trackStarted;
- trackFinished;
- skip;
- получение следующей sequence через /rotor/session/{radioSessionId}/tracks;
- короткое автоматическое воспроизведение;
- продолжение одной и той же radio session.

После этого сравним rotor/session с уже проверенным
rotor/station/user:onyourwave и только затем решим, какой вариант использовать
в SMR.

Это экспериментальный стенд, а не часть основной архитектуры приложения.
