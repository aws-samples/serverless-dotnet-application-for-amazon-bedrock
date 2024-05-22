// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

var session_id;

var socket = $.simpleWebSocket(
    {
        url: bedrock_backend_endpoint + backend_stage + "?token=" + cognito_id_token,
        timeout: 20000,
        attempts: 20,
        dataType: "json",
        onOpen: function (event) { connection_opened(); },
        onClose: function (event) { connection_closed(); },
        onError: function (event) { connection_error(); }
    }
);

socket.connect();

socket.listen(function (data) {
    if ('Role' in data && 'Response' in data) {
        add_message(data.Role, data.Response.trim());

        if ('RagSession' in data && data.RagSession.length > 0) {
            $('#option_rag_session').val(data.RagSession);
        }
    } else if ('message' in data) {
        add_message("system", data.message);
    }
});

function connection_opened() {
    session_id = generate_session_id();

    $('#chat-connection-status').remove();
    add_notification("Successfully connected with an AI");
    add_notification("You are now using " + $('#option_model').find("option:selected").text() + " foundational model");

    $('#chat-sendbutton').removeAttr("disabled");
    $('#chat-sendbutton').on("click", send_message);

    $('#chat-message').removeAttr("disabled");
    $('#chat-message').on("keyup", function (e) {
        if (e.key == "Enter") {
            e.preventDefault();
            send_message();
        }
    });

    $('.bedrock-options').each(function () {
        $(this).removeAttr("disabled");
    });

    $('#option_model').on("change", function () {
        $('#chat-body').html("");
        add_notification("You are now using " + $(this).find("option:selected").text() + " foundational model");
        session_id = generate_session_id();
    });
}

function connection_closed() {
    add_notification("Connection closed");

    $('#chat-sendbutton').attr("disabled", "disabled");
    $('#chat-message').val("");
    $('#chat-message').attr("disabled", "disabled");
}

function connection_error() {
    add_notification("Error connecting to an AI");
}

function add_notification(msg) {
    var p = $("<p>", {
        "class": "text-center mx-3 mb-0",
        "style": "color:#a2aab7"
    });
    $(p).append(msg);

    var d = $("<div>", {
        "class": "divider d-flex align-items-center mb-4"
    });
    $(d).append($(p));

    $('#chat-body').append($(d));
    $('#chat-body').animate({ scrollTop: $('#chat-body').prop("scrollHeight") }, 1000);
}

function add_message(role, msg) {
    $('.ai-waiting').remove();

    var d = $("<div>", {
        "class": "d-flex flex-row"
    });

    var d_nested = $("<div>");

    var p = $("<p>", {
        "class": "small p-2 ms-3 mb-1 rounded-3"
    });

    var i = $("<img>", {
        "class": "pretty-avatar"
    });

    switch (role) {
        case "user":
            d.addClass("justify-content-end mb-4 pt-1 msg-user");
            p.addClass("me-3 text-white bg-primary");
            i.prop("src", "/images/avatars/user.webp");
            break;
        case "assistant":
            d.addClass("justify-content-start msg-assistant");
            p.css("background-color", "#f5f6f7");
            i.prop("src", "/images/avatars/assistant.webp");
            break;
        default:
            d.addClass("justify-content-start msg-system");
            p.css("background-color", "#f5f6f7");
            i.prop("src", "/images/avatars/system.webp");
    }

    add_message_inline(p, msg);

    if ($('#chat-body div:last-child').hasClass("msg-" + role)) {
        $('#chat-body div:last-child div').append($(p));
    } else {
        $(d_nested).append($(p));

        if (role == "user")
            $(d).append($(d_nested)).append($(i));
        else
            $(d).append($(i)).append($(d_nested));

        $('#chat-body').append($(d));
    }

    $('#chat-body').animate({ scrollTop: $('#chat-body').prop("scrollHeight") }, 1000);
}

function add_ai_waiting() {
    var d = $("<div>", {
        "class": "d-flex flex-row justify-content-start mb-4 pt-1 ai-waiting"
    });

    var i = $("<img>", {
        "src": "/images/avatars/assistant.webp",
        "class": "pretty-avatar",
        "style": "width: 45px; height: 100%"
    });

    $(d).append($(i)).append("<div class=\"loader\"><span></span><span></span><span></span></div>");

    $('#chat-body').append($(d));
    $('#chat-body').animate({ scrollTop: $('#chat-body').prop("scrollHeight") }, 1000);
}

function send_message() {
    var msg = $('#chat-message').val();

    if (msg.length > 0) {
        data = {
            "action": action_name,
            "MessageGroupId": session_id,
            "message": msg,
            "params": generate_fm_params()
        }

        socket.send(data).done(function () {
            $('#chat-message').val('');
            add_message("user", msg);
            add_ai_waiting();
        }).fail(function (e) {
            add_notification("Error: could not send the last message");
            console.log(e);
        });
    }
}

function generate_session_id() {
    const timestamp = Date.now();
    const randomNumber = Math.floor(Math.random() * 99999);

    return `${timestamp}${randomNumber}`;
};

// Option Sidebar
$('#sidebar-open, #sidebar-close, #bedrock-option-overlay').on("click", function () {
    $('.bedrock-option-sidebar').toggleClass('bedrock-option-sidebar-active');
    $('.bedrock-option-overlay').toggleClass('bedrock-option-overlay-active');
});