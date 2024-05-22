// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: MIT-0

function generate_fm_params() {
    var params = {};

    $('.bedrock-options').each(function () {
        if ($(this).val() != null) {
            params[$(this).attr('id')] = $(this).val();
        }
    });

    return params;
}

const isValidUrl = urlString => {
    try {
        return Boolean(new URL(urlString));
    }
    catch (e) {
        return false;
    }
}

function add_message_inline(p, msg) {
    if ((msg.startsWith("http://") || msg.startsWith("https://")) && isValidUrl(msg))
    {
        var img = $("<img>");
        img.prop("src", msg);
        img.css("max-height", "150px");

        var link = $("<a>");
        link.prop("href", msg);
        link.prop("target", "_blank");
        link.html(img);

        $(p).append(link);
    }
    else
    {
        $(p).append(msg);
    }
}

$("#option_cfgscale").on("input change", function () {
    $('#current_cfgscale').html($(this).val());
});

function generate_resolutions_titan() {
    var resolutions_titan = ["512x512", "1024x1024", "768x768", "768x1152", "384x576", "1152x768", "576x384", "768x1280", "384x640", "1280x768", "640x384", "896x1152", "448x576", "1152x896", "576x448", "768x1408", "384x704", "1408x768", "704x384", "640x1408", "320x704", "1408x640", "704x320", "1152x640", "1173x640"];
    $("#option_resolution").empty();
    resolutions_titan.forEach(function (i) {
        $("#option_resolution").append(new Option(i, i));
    });
}

function generate_resolutions_stability() {
    var resolutions_stability = ["1024x1024", "1152x896", "1216x832", "1344x768", "1536x640", "640x1536", "768x1344", "832x1216", "896x1152"];
    $("#option_resolution").empty();
    resolutions_stability.forEach(function (i) {
        $("#option_resolution").append(new Option(i, i));
    });
}

$(function () {
    generate_resolutions_titan();
});

$("#option_model").on("change", function () {
    if ($(this).val() == "amazon.titan-image-generator-v1") {
        generate_resolutions_titan();

        $("#option_cfgscale").attr("min", "1.1");
        $("#option_cfgscale").attr("max", "10");
        $("#option_cfgscale").val("8.0");
        $("#current_cfgscale").html("8.0");

        $("label[for='option_sampler']").hide();
        $("#option_sampler").hide();
        $('#option_sampler').val(null);

        $("label[for='option_style_preset']").hide();
        $("#option_style_preset").hide();
        $('#option_style_preset').val(null);
    } else if ($(this).val() == "stability.stable-diffusion-xl-v1") {
        generate_resolutions_stability();

        $("#option_cfgscale").attr("min", "0");
        $("#option_cfgscale").attr("max", "35");
        $("#option_cfgscale").val("7.0");
        $("#current_cfgscale").html("7.0");

        $("label[for='option_sampler']").show();
        $("#option_sampler").show();

        $("label[for='option_style_preset']").show();
        $("#option_style_preset").show();
    }
});