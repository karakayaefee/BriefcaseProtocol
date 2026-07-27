# EOS Voice kurulumu

Bu proje oyun ağı için Unity Multiplayer/Netcode kullanmaya devam eder. Sesli iletişim ise Epic Online Services (EOS) Lobby + RTC üzerinden çalışır.

## Epic Developer Portal

1. Epic Developer Portal'da bir Product oluşturun veya mevcut ürünü açın.
2. Product Settings altında en az bir Sandbox ve Deployment oluşturun.
3. Clients bölümünde oyun için bir Client oluşturun.
4. Client Policy içinde Connect, Lobby ve RTC/Voice işlemleri için gereken izinleri açın.
5. Voice bölümünü etkinleştirin ve `createLobbyConference` iznini açın.
6. Product ID, Sandbox ID, Deployment ID, Client ID ve Client Secret değerlerini alın.

Kimlik bilgilerini sohbet mesajına, ekran görüntüsüne veya bu belgeye yazmayın. Değerleri yalnızca EOS yapılandırma ekranına girin. Eklenti, istemcinin çalışması için bunları `Assets/StreamingAssets/EOS` altındaki çalışma zamanı yapılandırmasına kaydeder; gerçek güvenlik sınırını dar kapsamlı Client Policy izinleri oluşturur.

## Unity

1. Unity menüsünden `EOS Plugin > EOS Configuration` ekranını açın.
2. Şu alanları doldurun:
   - Product Name
   - Product ID
   - Sandbox ID
   - Deployment ID
   - Client ID
   - Client Secret
   - Product Version (örneğin `1.0.0`)
3. Yapılandırmayı kaydedin ve Unity'nin yeniden derlemesini bekleyin.
4. MainMenu sahnesinden iki farklı istemciyle aynı oyun lobisine katılın.

`VoiceChatManager`, çalışma anında `EOSManager` oluşturduğu için sahneye ayrıca EOS prefabı yerleştirmek gerekmez.

## Oyun içi kullanım

- `V`: basılı tutarak konuşma (push-to-talk)
- `M`: gelen tüm sesi kapatma/açma
- Ses panelindeki `Mute/Unmute`: yalnızca seçilen oyuncunun sesini yerel olarak kapatma/açma
- Lobby sahnesinde herkes aynı ses odasına girer.
- Game sahnesinde oyuncular takımına göre ayrı EOS ses odalarına girer.

## Test notları

- İki istemciyi aynı Windows kullanıcı profili altında çalıştırmak EOS Device ID kimliğini paylaşabilir. En güvenilir test iki ayrı cihaz veya iki ayrı işletim sistemi kullanıcı profiliyle yapılır.
- Mikrofon izni işletim sistemi tarafından engellenmemiş olmalıdır.
- Konsolda `[VoiceChat/EOS]` mesajını ve oyuncu listesini kontrol edin.
- Yapılandırma eksikse ses yöneticisi oyun akışını durdurmaz; panelde yapılandırma hatasını gösterir.
