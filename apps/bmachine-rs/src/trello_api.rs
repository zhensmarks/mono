use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloBoardItem {
    pub id: String,
    pub name: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloListItem {
    pub id: String,
    pub name: String,
    pub id_board: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[allow(dead_code)]
pub struct TrelloCardItem {
    pub id: String,
    pub name: String,
    pub desc: Option<String>,
    pub due: Option<String>,
    #[serde(rename = "shortUrl")]
    pub short_url: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TrelloMember {
    #[serde(rename = "fullName")]
    pub full_name: Option<String>,
    pub username: String,
}

pub struct TrelloApiService;

impl TrelloApiService {
    #[allow(dead_code)]
    pub fn fetch_boards(api_key: &str, token: &str) -> Result<Vec<TrelloBoardItem>, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "https://api.trello.com/1/members/me/boards?key={}&token={}&fields=name,id",
            api_key, token
        );

        let res = client.get(&url).send().map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello API error: {}", res.status()));
        }

        let boards: Vec<TrelloBoardItem> = res.json().map_err(|e| format!("JSON parse error: {}", e))?;
        Ok(boards)
    }

    #[allow(dead_code)]
    pub fn fetch_lists(api_key: &str, token: &str, board_id: &str) -> Result<Vec<TrelloListItem>, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "https://api.trello.com/1/boards/{}/lists?key={}&token={}&fields=name,id,idBoard",
            board_id, api_key, token
        );

        let res = client.get(&url).send().map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello API error: {}", res.status()));
        }

        let lists: Vec<TrelloListItem> = res.json().map_err(|e| format!("JSON parse error: {}", e))?;
        Ok(lists)
    }

    pub fn verify_connection(api_key: &str, token: &str) -> Result<TrelloMember, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "https://api.trello.com/1/members/me?key={}&token={}&fields=fullName,username",
            api_key.trim(),
            token.trim()
        );

        let res = client.get(&url).send().map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello auth gagal ({})", res.status()));
        }
        res.json::<TrelloMember>()
            .map_err(|e| format!("JSON parse error: {}", e))
    }

    pub fn fetch_cards(api_key: &str, token: &str, list_id: &str) -> Result<Vec<TrelloCardItem>, String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;

        let url = format!(
            "https://api.trello.com/1/lists/{}/cards?key={}&token={}&fields=name,desc,due,id,shortUrl",
            list_id.trim(),
            api_key.trim(),
            token.trim()
        );

        let res = client.get(&url).send().map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Trello API error: {}", res.status()));
        }

        let cards: Vec<TrelloCardItem> = res.json().map_err(|e| format!("JSON parse error: {}", e))?;
        Ok(cards)
    }

    #[allow(dead_code)]
    pub fn add_comment(api_key: &str, token: &str, card_id: &str, text: &str) -> Result<(), String> {
        let client = reqwest::blocking::Client::builder()
            .timeout(std::time::Duration::from_secs(10))
            .build()
            .map_err(|e| e.to_string())?;
        let url = format!(
            "https://api.trello.com/1/cards/{}/actions/comments?key={}&token={}",
            card_id.trim(),
            api_key.trim(),
            token.trim()
        );
        let res = client
            .post(&url)
            .form(&[("text", text)])
            .send()
            .map_err(|e| format!("HTTP request gagal: {}", e))?;
        if !res.status().is_success() {
            return Err(format!("Gagal kirim komentar: {}", res.status()));
        }
        Ok(())
    }
}
