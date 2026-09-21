(() => {
  const texts = {
    en: {
      name: "Display name",
      join: "Join",
      leave: "Leave",
      mute: "Mute",
      unmute: "Unmute",
      deaf: "Deafen",
      undeaf: "Undeafen",
      people: (n) => (n === 1 ? "1 person" : `${n} people`),
      missing: "This voice room is not available.",
      audioOff: "Joined the room, but audio is not connected.",
      failed: "Could not join voice.",
    },
    zh: {
      name: "显示名称",
      join: "加入",
      leave: "离开",
      mute: "静音",
      unmute: "取消静音",
      deaf: "耳聋",
      undeaf: "取消耳聋",
      people: (n) => `${n} 人`,
      missing: "这个语音房间不可用。",
      audioOff: "已进入房间，但音频未接通。",
      failed: "无法加入语音。",
    },
    ja: {
      name: "表示名",
      join: "参加",
      leave: "退出",
      mute: "ミュート",
      unmute: "ミュート解除",
      deaf: "スピーカーオフ",
      undeaf: "スピーカーオン",
      people: (n) => `${n} 人`,
      missing: "このボイスルームは利用できません。",
      audioOff: "入室しましたが、音声に接続できません。",
      failed: "ボイスに参加できません。",
    },
  };
  const lang = navigator.language.startsWith("zh") ? "zh" : navigator.language.startsWith("ja") ? "ja" : "en";
  const t = texts[lang];
  const channelId = location.pathname.split("/").filter(Boolean)[1] || "";
  const storeKey = `chat.voice.guest.${channelId}`;
  const $ = (id) => document.getElementById(id);
  const nameInput = $("name");
  const gate = $("gate");
  const call = $("call");
  const status = $("status");
  const people = $("people");
  let token = "";
  let me = "";
  let mute = false;
  let deaf = false;
  let room = null;
  let poll = 0;

  $("nameLabel").textContent = t.name;
  $("join").textContent = t.join;
  $("leave").textContent = t.leave;
  document.documentElement.lang = lang === "zh" ? "zh-Hans" : lang;

  function showStatus(text) {
    status.hidden = !text;
    status.textContent = text || "";
  }

  function flags(item) {
    const bits = [];
    if (item.self_mute) bits.push(t.mute);
    if (item.self_deaf) bits.push(t.deaf);
    return bits.join(" · ");
  }

  function renderPeople(items) {
    people.replaceChildren();
    for (const item of items) {
      const row = document.createElement("li");
      const name = document.createElement("span");
      name.textContent = item.display_name + (item.user_id === me ? " ·" : "");
      const meta = document.createElement("span");
      meta.className = "meta";
      meta.textContent = flags(item);
      row.append(name, meta);
      people.append(row);
    }
    $("count").textContent = t.people(items.length);
  }

  async function api(path, options) {
    const headers = { ...(options?.headers || {}) };
    if (token) headers.Authorization = `Bearer ${token}`;
    if (options?.body && !headers["Content-Type"]) headers["Content-Type"] = "application/json";
    const response = await fetch(path, { ...options, headers });
    if (response.status === 204) return null;
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw Object.assign(new Error(body.message || t.failed), { status: response.status, body });
    return body;
  }

  async function connectRtc(join) {
    if (!window.LivekitClient) throw new Error(t.audioOff);
    room = new LivekitClient.Room();
    await room.connect(join.rtc.url, join.rtc.token);
    await room.localParticipant.setMicrophoneEnabled(!mute && !deaf);
    room.on(LivekitClient.RoomEvent.Disconnected, () => {
      if (token) showStatus(t.audioOff);
    });
  }

  async function refreshPeople() {
    const items = await api(`/api/v1/channels/${channelId}/voice-states`);
    renderPeople(items);
  }

  async function enter(session) {
    token = session.access_token;
    me = session.user.id;
    mute = session.join.state.self_mute;
    deaf = session.join.state.self_deaf;
    sessionStorage.setItem(storeKey, JSON.stringify({ token, user: session.user }));
    $("room").textContent = session.room.name;
    document.title = session.room.name;
    gate.hidden = true;
    call.hidden = false;
    paintButtons();
    renderPeople([session.join.state]);
    try {
      await connectRtc(session.join);
      showStatus("");
    } catch {
      showStatus(t.audioOff);
    }
    await refreshPeople().catch(() => {});
    clearInterval(poll);
    poll = setInterval(() => refreshPeople().catch(() => {}), 2000);
  }

  function paintButtons() {
    $("mute").textContent = mute ? t.unmute : t.mute;
    $("deaf").textContent = deaf ? t.undeaf : t.deaf;
  }

  async function patch(nextMute, nextDeaf) {
    if (nextDeaf) nextMute = true;
    const state = await api("/api/v1/voice/state", {
      method: "PATCH",
      body: JSON.stringify({ self_mute: nextMute, self_deaf: nextDeaf }),
    });
    mute = state.self_mute;
    deaf = state.self_deaf;
    paintButtons();
    if (room) {
      await room.localParticipant.setMicrophoneEnabled(!mute && !deaf);
      document.querySelectorAll("audio").forEach((audio) => {
        audio.muted = deaf;
      });
    }
    await refreshPeople().catch(() => {});
  }

  async function leave() {
    clearInterval(poll);
    poll = 0;
    try {
      await api("/api/v1/voice/leave", { method: "POST" });
    } catch {
      // Already gone.
    }
    sessionStorage.removeItem(storeKey);
    token = "";
    me = "";
    if (room) {
      room.disconnect();
      room = null;
    }
    gate.hidden = false;
    call.hidden = true;
    showStatus("");
  }

  gate.addEventListener("submit", async (event) => {
    event.preventDefault();
    $("join").disabled = true;
    showStatus("");
    try {
      const session = await api(`/api/v1/voice/rooms/${channelId}/guest`, {
        method: "POST",
        body: JSON.stringify({ display_name: nameInput.value.trim(), self_mute: mute, self_deaf: deaf }),
      });
      await enter(session);
    } catch (error) {
      showStatus(error.message || t.failed);
    } finally {
      $("join").disabled = false;
    }
  });

  $("mute").addEventListener("click", () => patch(!mute, deaf));
  $("deaf").addEventListener("click", () => patch(mute || !deaf, !deaf));
  $("leave").addEventListener("click", () => leave());
  window.addEventListener("pagehide", () => {
    if (!token) return;
    fetch("/api/v1/voice/leave", {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      keepalive: true,
    });
  });

  (async () => {
    try {
      const info = await fetch("/.well-known/lightchat").then((r) => r.json());
      $("instance").textContent = info.name || "";
    } catch {
      $("instance").textContent = "";
    }
    try {
      const roomInfo = await api(`/api/v1/voice/rooms/${channelId}`);
      $("room").textContent = roomInfo.name;
      document.title = roomInfo.name;
      $("count").textContent = t.people(roomInfo.participant_count);
    } catch {
      gate.hidden = true;
      showStatus(t.missing);
      return;
    }
    const saved = sessionStorage.getItem(storeKey);
    if (!saved) return;
    try {
      const parsed = JSON.parse(saved);
      token = parsed.token || "";
      if (parsed.user?.display_name) nameInput.value = parsed.user.display_name;
      if (!token || !parsed.user) throw new Error("expired");
      const join = await api(`/api/v1/channels/${channelId}/voice/join`, {
        method: "POST",
        body: JSON.stringify({ self_mute: mute, self_deaf: deaf }),
      });
      await enter({
        access_token: token,
        expires_in: 0,
        user: parsed.user,
        room: { name: $("room").textContent, participant_count: 0 },
        join,
      });
    } catch {
      sessionStorage.removeItem(storeKey);
      token = "";
    }
  })();
})();
