// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// site.js
function sendMessage() {
    var userInput = document.getElementById("user-input").value;
    var messagesDiv = document.getElementById("messages");

    // Display user's message
    messagesDiv.innerHTML += `<div>User: ${userInput}</div>`;

    // Get the bot's response
    fetch('/home/getbotresponse?userInput=' + userInput)
        .then(response => response.text())
        .then(data => {
            messagesDiv.innerHTML += `<div>Bot: ${data}</div>`;
        });

    // Clear the user input field
    document.getElementById("user-input").value = '';
}
