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

pub struct TrelloApiService;

impl TrelloApiService {
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
}
