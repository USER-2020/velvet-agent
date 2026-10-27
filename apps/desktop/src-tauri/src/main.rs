#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine};
use futures_util::{SinkExt, StreamExt};
use rand::RngCore;
use serde::Deserialize;
use serde_json::{json, Value};
use sha2::Digest;
use std::sync::OnceLock;
use tauri::Manager;
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use tokio::net::TcpListener;
use url::Url;

// Solo puede existir un flujo OAuth activo por estación. Sin este guard,
// dos clics o una segunda invocación del frontend generan dos códigos y el
// primero puede quedar sin consumir mientras el segundo reemplaza la UI.
static OAUTH_LOGIN_LOCK: OnceLock<tokio::sync::Mutex<()>> = OnceLock::new();
#[tauri::command]
async fn agent_request(operation: String, token: Option<String>) -> Result<Value, String> {
    if ![
        "GetAgentStatus",
        "GetSystemMetrics",
        "GetObsStatus",
        "GetConfiguration",
        "RequestEnrollment",
        "UpdateObsStatus",
        "StartTransmission",
        "StopTransmission",
    ]
    .contains(&operation.as_str())
    {
        return Err("Operación no permitida".into());
    }
    if token
        .as_ref()
        .is_some_and(|s| s.len() > 4096 || s.chars().any(char::is_control))
    {
        return Err("Token inválido".into());
    }
    let message = json!({"Version":1,"Operation":operation,"Token":token}).to_string() + "\n";
    tokio::time::timeout(std::time::Duration::from_secs(12), exchange(message))
        .await
        .map_err(|_| "Servicio sin respuesta".to_string())?
}
#[tauri::command]
async fn report_obs_status(status: Value) -> Result<Value, String> {
    if !status.is_object() {
        return Err("Estado OBS inválido".into());
    }
    let message = json!({"Version":1,"Operation":"UpdateObsStatus","Obs":status,"Token":null})
        .to_string()
        + "\n";
    tokio::time::timeout(std::time::Duration::from_secs(12), exchange(message))
        .await
        .map_err(|_| "Servicio sin respuesta".to_string())?
}
#[derive(Deserialize)]
struct OAuthResponse {
    access_token: String,
    refresh_token: String,
    expires_in: f64,
    refresh_expires_in: f64,
}
#[tauri::command]
async fn oauth_login(
    master_url: String,
    installation_id: String,
    hostname: String,
    studio_id: String,
) -> Result<Value, String> {
    let lock = OAUTH_LOGIN_LOCK.get_or_init(|| tokio::sync::Mutex::new(()));
    let _guard = lock
        .try_lock()
        .map_err(|_| "Ya hay un inicio de sesión OAuth en curso".to_string())?;
    let master_origin = normalize_master_url(&master_url)?;
    let mut base = Url::parse(&master_origin).map_err(|_| "URL de Master inválida")?;
    let listener = TcpListener::bind("127.0.0.1:0")
        .await
        .map_err(|_| "No se pudo abrir callback local")?;
    let port = listener
        .local_addr()
        .map_err(|_| "Puerto de callback inválido")?
        .port();
    let redirect = format!("http://127.0.0.1:{port}/oauth/callback");
    let mut verifier_bytes = [0_u8; 32];
    rand::rng().fill_bytes(&mut verifier_bytes);
    let verifier = URL_SAFE_NO_PAD.encode(verifier_bytes);
    let state = URL_SAFE_NO_PAD.encode(rand::random::<[u8; 32]>());
    let challenge = URL_SAFE_NO_PAD.encode(sha2::Sha256::digest(verifier.as_bytes()));
    base.set_path("/oauth/agent/authorize");
    base.query_pairs_mut()
        .append_pair("client_id", "velvet-agent")
        .append_pair("redirect_uri", &redirect)
        .append_pair("response_type", "code")
        .append_pair("installation_id", &installation_id)
        .append_pair("hostname", &hostname)
        .append_pair("studio_id", &studio_id)
        .append_pair("code_challenge", &challenge)
        .append_pair("code_challenge_method", "S256")
        .append_pair("state", &state);
    eprintln!("[oauth] callback escuchando en {redirect}");
    open_browser(base.as_str())?;
    let deadline = tokio::time::Instant::now() + std::time::Duration::from_secs(180);
    let (mut stream, target) = loop {
        let remaining = deadline.saturating_duration_since(tokio::time::Instant::now());
        if remaining.is_zero() {
            return Err("Tiempo de autenticación agotado".into());
        }
        let (mut candidate, _) = tokio::time::timeout(remaining, listener.accept())
            .await
            .map_err(|_| "Tiempo de autenticación agotado")?
            .map_err(|_| "Callback inválido")?;
        let mut request = [0_u8; 8192];
        let count = match tokio::time::timeout(
            std::time::Duration::from_secs(3),
            candidate.read(&mut request),
        )
        .await
        {
            Ok(Ok(count)) if count > 0 => count,
            _ => continue,
        };
        let request_text = String::from_utf8_lossy(&request[..count]);
        let line = request_text.lines().next().unwrap_or("");
        let Some(target) = line
            .strip_prefix("GET ")
            .and_then(|v| v.split_whitespace().next())
        else {
            continue;
        };
        break (candidate, target.to_string());
    };
    eprintln!("[oauth] callback recibido en {redirect}");
    let callback =
        Url::parse(&format!("http://127.0.0.1{target}")).map_err(|_| "Callback inválido")?;
    if callback.path() != "/oauth/callback" {
        eprintln!("[oauth] ruta de callback inválida: {}", callback.path());
        return Err("Ruta de callback inválida".into());
    }
    let callback_state = callback
        .query_pairs()
        .find(|(k, _)| k == "state")
        .map(|(_, v)| v.into_owned());
    if callback_state.as_deref() != Some(state.as_str()) {
        eprintln!(
            "[oauth] state inválido o ausente (recibido={})",
            callback_state.is_some()
        );
        return Err("Estado OAuth inválido".into());
    }
    let code = callback
        .query_pairs()
        .find(|(k, _)| k == "code")
        .map(|(_, v)| v.into_owned())
        .ok_or_else(|| {
            eprintln!("[oauth] callback sin código");
            "Master no devolvió código"
        })?;
    let response = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nConnection: close\r\n\r\n<!doctype html><meta charset=\"utf-8\"><title>VELVET Agent</title><p>Autenticación completada. Puedes volver a VELVET Agent.</p><script>window.close()</script>";
    let _ = stream.write_all(response.as_bytes()).await;
    let token_url = format!("{master_origin}/api/agent/oauth/token");
    let client = reqwest::Client::builder()
        .timeout(std::time::Duration::from_secs(20))
        .build()
        .map_err(|_| "No se pudo preparar la conexión con Master")?;
    eprintln!("[oauth] intercambiando código por tokens");
    let token_response = client
        .post(token_url)
        .form(&[
            ("grant_type", "authorization_code"),
            ("client_id", "velvet-agent"),
            ("code", code.as_str()),
            ("redirect_uri", redirect.as_str()),
            ("code_verifier", verifier.as_str()),
        ])
        .send()
        .await
        .map_err(|e| {
            eprintln!("[oauth] error contactando token: {e}");
            "No se pudo contactar Master".to_string()
        })?;
    let token_status = token_response.status();
    if !token_status.is_success() {
        let detail = token_response.text().await.unwrap_or_default();
        eprintln!(
            "[oauth] Master token respondió {token_status}: {}",
            &detail[..detail.len().min(500)]
        );
        return Err("Master rechazó la autenticación".into());
    }
    // Read the body first so a proxy/Laravel error page is visible in the
    // desktop log instead of being reported only as a generic JSON decode
    // failure. This also makes the OAuth contract mismatch diagnosable.
    let token_body = token_response.text().await.map_err(|e| {
        eprintln!("[oauth] no se pudo leer respuesta token: {e}");
        "Respuesta OAuth inválida".to_string()
    })?;
    let tokens: OAuthResponse = serde_json::from_str(&token_body).map_err(|e| {
        let preview = &token_body[..token_body.len().min(1000)];
        eprintln!("[oauth] respuesta token inválida ({e}); cuerpo: {preview}");
        "Respuesta OAuth inválida".to_string()
    })?;
    eprintln!("[oauth] tokens recibidos; consultando usuario");
    // Obtiene el usuario asociado al token para que el servicio pueda exponerlo
    // al Desktop en GetAgentStatus y la sesión no quede como un simple booleano.
    let me: Value = client
        .get(format!("{master_origin}/api/agent/me"))
        .bearer_auth(&tokens.access_token)
        .send()
        .await
        .map_err(|_| "No se pudo consultar el usuario en Master")?
        .error_for_status()
        .map_err(|_| "Master rechazó la consulta del usuario")?
        .json()
        .await
        .map_err(|_| "Respuesta de usuario inválida")?;
    let user = me.get("user").ok_or("Master no devolvió el usuario")?;
    let station_response: Value = client
        .get(format!("{master_origin}/api/agent/station"))
        .bearer_auth(&tokens.access_token)
        .send()
        .await
        .map_err(|_| "No se pudo consultar la estación en Master")?
        .error_for_status()
        .map_err(|_| "Master rechazó la consulta de la estación")?
        .json()
        .await
        .map_err(|_| "Respuesta de estación inválida")?;
    let station = station_response.get("station").unwrap_or(&station_response);
    let station_id = station
        .get("id")
        .and_then(Value::as_i64)
        .ok_or("Master no devolvió el identificador de estación")?;
    validate_station(station, &installation_id, &studio_id)?;
    // Laravel serializa el identificador como número; aceptar también texto
    // evita perder la asociación usuario–estación al guardar la sesión.
    let user_id = user
        .get("id")
        .map(|value| {
            value
                .as_str()
                .map(str::to_owned)
                .unwrap_or_else(|| value.to_string())
        })
        .unwrap_or_default();
    let display_name = user
        .get("name")
        .and_then(Value::as_str)
        .or_else(|| user.get("email").and_then(Value::as_str))
        .unwrap_or("Usuario VELVET");
    let permissions: Vec<String> = user
        .get("permissions")
        .and_then(Value::as_array)
        .map(|values| {
            values
                .iter()
                .filter_map(Value::as_str)
                .map(str::to_owned)
                .collect()
        })
        .unwrap_or_default();
    if !permissions
        .iter()
        .any(|permission| permission == "stations.connect")
    {
        return Err("El usuario no tiene permiso stations.connect".into());
    }
    let roles: Vec<String> = user
        .get("roles")
        .and_then(Value::as_array)
        .map(|values| {
            values
                .iter()
                .filter_map(|value| {
                    value
                        .as_str()
                        .or_else(|| value.get("name").and_then(Value::as_str))
                })
                .map(str::to_owned)
                .collect()
        })
        .unwrap_or_default();
    eprintln!(
        "[oauth] usuario recibido: id={user_id}, roles={:?}, permisos={:?}",
        roles, permissions
    );
    let configuration_available = me
        .pointer("/modules/configuration")
        .and_then(Value::as_bool)
        .or_else(|| {
            me.get("modules").and_then(Value::as_array).map(|modules| {
                modules
                    .iter()
                    .any(|module| module.as_str() == Some("configuration"))
            })
        })
        .unwrap_or_else(|| {
            permissions
                .iter()
                .any(|permission| permission == "stations.configure")
        });
    let access_seconds = tokens.expires_in.round() as i64;
    let refresh_seconds = tokens.refresh_expires_in.round() as i64;
    let session_id = random_session_id();
    let session_started_at = chrono::Utc::now();
    let auth = json!({"AccessToken":tokens.access_token,"RefreshToken":tokens.refresh_token,"AccessExpiresAt":(session_started_at + chrono::Duration::seconds(access_seconds)).to_rfc3339(),"RefreshExpiresAt":(session_started_at + chrono::Duration::seconds(refresh_seconds)).to_rfc3339(),"UserId":user_id,"DisplayName":display_name,"Permissions":permissions,"Roles":roles,"ConfigurationAvailable":configuration_available,"SessionId":session_id,"SessionStartedAt":session_started_at.to_rfc3339(),"StationId":station_id,"MasterUrl":master_origin});
    let message = json!({"Version":1,"Operation":"SetOAuthSession","Auth":auth,"Token":null})
        .to_string()
        + "\n";
    let response = tokio::time::timeout(std::time::Duration::from_secs(12), exchange(message))
        .await
        .map_err(|_| "Servicio sin respuesta")??;
    if response.get("Success") != Some(&Value::Bool(true)) {
        return Err("El servicio no pudo guardar la sesión".into());
    }
    eprintln!("[oauth] sesión guardada en el servicio");
    Ok(json!({"Authenticated":true,"AccessExpiresIn":access_seconds}))
}
fn normalize_master_url(value: &str) -> Result<String, String> {
    let mut url = Url::parse(value).map_err(|_| "URL de Master inválida")?;
    if url.scheme() != "https"
        || url.host_str().is_none()
        || url.username() != ""
        || url.password().is_some()
        || url.query().is_some()
        || url.fragment().is_some()
        || (url.path() != "" && url.path() != "/")
    {
        return Err("Master debe ser únicamente un dominio HTTPS".into());
    }
    url.set_path("");
    Ok(url.to_string().trim_end_matches('/').to_string())
}

fn validate_station(station: &Value, installation_id: &str, studio_id: &str) -> Result<(), String> {
    let returned_installation = station
        .get("installationId")
        .and_then(Value::as_str)
        .ok_or("Master no devolvió station.installationId")?;
    if returned_installation != installation_id {
        return Err("La estación devuelta por Master no coincide con esta instalación".into());
    }
    let returned_studio = station
        .get("studioId")
        .and_then(Value::as_str)
        .ok_or("Master no devolvió station.studioId")?;
    if returned_studio != studio_id {
        return Err(
            "La estación devuelta por Master no coincide con el estudio configurado".into(),
        );
    }
    Ok(())
}

fn open_browser(target: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let mut c = std::process::Command::new("cmd");
        c.args(["/C", "start", "", target]);
        return c
            .status()
            .map_err(|_| "No se pudo abrir el navegador".to_string())
            .and_then(|s| {
                if s.success() {
                    Ok(())
                } else {
                    Err("No se pudo abrir el navegador".into())
                }
            });
    }

    #[cfg(target_os = "macos")]
    let mut command = std::process::Command::new("open");
    #[cfg(target_os = "linux")]
    let mut command = std::process::Command::new("xdg-open");

    #[cfg(any(target_os = "macos", target_os = "linux"))]
    {
        command
            .arg(target)
            .status()
            .map_err(|_| "No se pudo abrir el navegador".to_string())
            .and_then(|s| {
                if s.success() {
                    Ok(())
                } else {
                    Err("No se pudo abrir el navegador".into())
                }
            })
    }

    #[cfg(not(any(target_os = "windows", target_os = "macos", target_os = "linux")))]
    {
        let _ = target;
        Err("Sistema operativo no compatible para abrir el navegador".into())
    }
}
fn random_session_id() -> String {
    let hex = format!("{:032x}", rand::random::<u128>());
    format!(
        "{}-{}-{}-{}-{}",
        &hex[0..8],
        &hex[8..12],
        &hex[12..16],
        &hex[16..20],
        &hex[20..32]
    )
}
async fn exchange(message: String) -> Result<Value, String> {
    #[cfg(windows)]
    let stream = tokio::net::windows::named_pipe::ClientOptions::new()
        .open(r"\\.\pipe\velvet-agent-v1")
        .map_err(|_| "Servicio no disponible")?;
    #[cfg(unix)]
    let stream =
        tokio::net::UnixStream::connect(std::env::temp_dir().join("CoreFxPipe_velvet-agent-v1"))
            .await
            .map_err(|_| "Servicio no disponible")?;
    let (reader, mut writer) = tokio::io::split(stream);
    writer
        .write_all(message.as_bytes())
        .await
        .map_err(|_| "Error IPC")?;
    let mut reader = BufReader::new(reader);
    let mut output = Vec::new();
    loop {
        let data = reader.fill_buf().await.map_err(|_| "Error IPC")?;
        if data.is_empty() {
            return Err("Respuesta incompleta".into());
        }
        let size = data
            .iter()
            .position(|b| *b == b'\n')
            .map(|i| i + 1)
            .unwrap_or(data.len());
        let done = data[size - 1] == b'\n';
        if output.len() + size > 65536 {
            return Err("Respuesta excesiva".into());
        }
        output.extend_from_slice(&data[..size]);
        reader.consume(size);
        if done {
            break;
        }
    }
    serde_json::from_slice(&output).map_err(|_| "Respuesta inválida".into())
}
#[tauri::command]
async fn obs_status() -> Result<Value, String> {
    tokio::time::timeout(std::time::Duration::from_secs(8), inspect_obs())
        .await
        .map_err(|_| "OBS sin respuesta".to_string())?
}
#[tauri::command]
async fn obs_set_streaming(start: bool) -> Result<Value, String> {
    tokio::time::timeout(std::time::Duration::from_secs(8), set_streaming(start))
        .await
        .map_err(|_| "OBS sin respuesta".to_string())?
}
async fn set_streaming(start: bool) -> Result<Value, String> {
    let (mut socket, _) = tokio_tungstenite::connect_async("ws://127.0.0.1:4455")
        .await
        .map_err(|_| "OBS desconectado".to_string())?;
    let hello = socket
        .next()
        .await
        .ok_or("Sin Hello")?
        .map_err(|_| "Error OBS")?;
    let hello: Value = serde_json::from_str(hello.to_text().map_err(|_| "Hello inválido")?)
        .map_err(|_| "JSON OBS inválido".to_string())?;
    if hello["op"] != 0 {
        return Err("Protocolo OBS inválido".into());
    }
    if hello["d"].get("authentication").is_some() {
        return Err("OBS requiere autenticación para controlar la transmisión".into());
    }
    socket
        .send(json!({"op":1,"d":{"rpcVersion":1}}).to_string().into())
        .await
        .map_err(|_| "Identify falló".to_string())?;
    let identified = socket
        .next()
        .await
        .ok_or("Sin Identified")?
        .map_err(|_| "Error OBS")?;
    let identified: Value =
        serde_json::from_str(identified.to_text().map_err(|_| "Identified inválido")?)
            .map_err(|_| "JSON inválido".to_string())?;
    if identified["op"] != 2 {
        return Err("OBS no identificado".into());
    }
    let request_type = if start { "StartStream" } else { "StopStream" };
    socket
        .send(
            json!({"op":6,"d":{"requestType":request_type,"requestId":"stream-control"}})
                .to_string()
                .into(),
        )
        .await
        .map_err(|_| "Petición OBS falló".to_string())?;
    loop {
        let msg = socket
            .next()
            .await
            .ok_or("OBS cerrado")?
            .map_err(|_| "Error OBS")?;
        if !msg.is_text() {
            continue;
        }
        let response: Value = serde_json::from_str(msg.to_text().map_err(|_| "Mensaje inválido")?)
            .map_err(|_| "JSON inválido".to_string())?;
        if response["op"] != 7 || response["d"]["requestId"] != "stream-control" {
            continue;
        }
        if response["d"]["requestStatus"]["result"] != true {
            return Err(response["d"]["requestStatus"]["comment"]
                .as_str()
                .unwrap_or("OBS rechazó la operación")
                .to_string());
        }
        let _ = socket.close(None).await;
        return Ok(json!({"Streaming": start}));
    }
}
async fn inspect_obs() -> Result<Value, String> {
    let (mut socket, _) = tokio_tungstenite::connect_async("ws://127.0.0.1:4455")
        .await
        .map_err(|_| "OBS desconectado")?;
    let hello = socket
        .next()
        .await
        .ok_or("Sin Hello")?
        .map_err(|_| "Error OBS")?;
    let hello: Value = serde_json::from_str(hello.to_text().map_err(|_| "Hello inválido")?)
        .map_err(|_| "JSON OBS inválido")?;
    if hello["op"] != 0 {
        return Err("Protocolo OBS inválido".into());
    }
    if hello["d"].get("authentication").is_some() {
        return Ok(
            json!({"ProcessRunning":true,"WebSocketConnected":false,"Streaming":null,"CurrentScene":null,"Scenes":null,"Error":"OBS requiere autenticación; configuración segura pendiente"}),
        );
    }
    socket
        .send(json!({"op":1,"d":{"rpcVersion":1}}).to_string().into())
        .await
        .map_err(|_| "Identify falló")?;
    let identified = socket
        .next()
        .await
        .ok_or("Sin Identified")?
        .map_err(|_| "Error OBS")?;
    let identified: Value =
        serde_json::from_str(identified.to_text().map_err(|_| "Identified inválido")?)
            .map_err(|_| "JSON inválido")?;
    if identified["op"] != 2 {
        return Err("OBS no identificado".into());
    }
    let mut result = json!({"ProcessRunning":true,"WebSocketConnected":true,"Streaming":null,"CurrentScene":null,"Scenes":null,"Error":null});
    for (id, kind) in [("stream", "GetStreamStatus"), ("scenes", "GetSceneList")] {
        socket
            .send(
                json!({"op":6,"d":{"requestType":kind,"requestId":id}})
                    .to_string()
                    .into(),
            )
            .await
            .map_err(|_| "Petición OBS falló")?;
        loop {
            let msg = socket
                .next()
                .await
                .ok_or("OBS cerrado")?
                .map_err(|_| "Error OBS")?;
            if !msg.is_text() {
                continue;
            }
            let v: Value = serde_json::from_str(msg.to_text().map_err(|_| "Mensaje inválido")?)
                .map_err(|_| "JSON inválido")?;
            if v["op"] != 7 || v["d"]["requestId"] != id {
                continue;
            }
            if v["d"]["requestStatus"]["result"] != true {
                return Err("Consulta OBS rechazada".into());
            }
            let data = &v["d"]["responseData"];
            if id == "stream" {
                result["Streaming"] = data["outputActive"].clone();
            } else {
                result["CurrentScene"] = data["currentProgramSceneName"].clone();
                result["Scenes"] = json!(data["scenes"]
                    .as_array()
                    .unwrap_or(&vec![])
                    .iter()
                    .filter_map(|s| s["sceneName"].as_str())
                    .collect::<Vec<_>>());
            }
            break;
        }
    }
    let _ = socket.close(None).await;
    Ok(result)
}
fn main() {
    tauri::Builder::default()
        .setup(|app| {
            let show = tauri::menu::MenuItem::with_id(
                app,
                "show",
                "Abrir VELVET Agent",
                true,
                None::<&str>,
            )?;
            let exit = tauri::menu::MenuItem::with_id(app, "exit", "Salir", true, None::<&str>)?;
            let menu = tauri::menu::Menu::with_items(app, &[&show, &exit])?;
            let icon = app
                .default_window_icon()
                .cloned()
                .ok_or_else(|| std::io::Error::other("Falta el icono de marca"))?;
            tauri::tray::TrayIconBuilder::new()
                .icon(icon)
                .tooltip("VELVET Agent")
                .menu(&menu)
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "show" => {
                        if let Some(w) = app.get_webview_window("main") {
                            let _ = w.show();
                            let _ = w.set_focus();
                        }
                    }
                    "exit" => app.exit(0),
                    _ => {}
                })
                .build(app)?;
            Ok(())
        })
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .invoke_handler(tauri::generate_handler![
            agent_request,
            report_obs_status,
            obs_status,
            obs_set_streaming,
            oauth_login
        ])
        .run(tauri::generate_context!())
        .expect("Error iniciando VELVET Agent");
}
